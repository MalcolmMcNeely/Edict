using Edict.Contracts;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.Streams.Core;

namespace Edict.Core.Idempotency;

/// <summary>
/// Abstract generic base for every event-consuming grain (event handlers,
/// projection builders, sagas — the shared inheritance root for the
/// idempotent-consumer family, brand-rule clause (b)). Owns the
/// stream-observer callback, suppresses at-least-once redeliveries via a
/// configurable bounded window of recently handled
/// <see cref="EdictEvent.EventId"/>s, and commits progress only after the
/// subclass's dispatch succeeds (ADR 0002). All outbox plumbing — drain
/// algorithm, lazy reminder, drain-on-activation — lives on the composed
/// <see cref="OutboxHost{TPayload}"/> field; the grain itself is a thin
/// Orleans lifecycle shell that forwards <c>OnActivateAsync</c> and
/// <c>ReceiveReminder</c>, plus the implicit-subscription stream observer
/// surface that's unique to this role.
/// <para>
/// The persisted document is the single-write <see cref="GrainEnvelope{TPayload}"/>
/// <c>{ Payload, Outbox, Idempotency }</c>: the dedup state is a sibling slot
/// (<see cref="GrainEnvelope{TPayload}.Idempotency"/>), the consumer payload is
/// the <see cref="GrainEnvelope{TPayload}.Payload"/> slot, and the Outbox slice
/// shares the same atomic write (ADR 0018).
/// </para>
/// <para>
/// Stream wiring is pure-implicit: the subclass carries
/// <c>[ImplicitStreamSubscription]</c> (emitted by the generator or applied by
/// hand on a test consumer), and the base implements
/// <see cref="IAsyncObserver{T}"/> + <see cref="IStreamSubscriptionObserver"/> /
/// <see cref="OnSubscribed"/> so the runtime binds delivery to the grain
/// directly. Mixing explicit <c>SubscribeAsync</c> with implicit subscriptions
/// — the trap that surfaced in #53 with memory streams and a referenced-assembly
/// consumer — is no longer possible because the subclass no longer participates
/// in stream wiring.
/// </para>
/// </summary>
[StorageProvider(ProviderName = "edict-state")]
public abstract class EdictIdempotencyBase<TPayload>
    : Grain<GrainEnvelope<TPayload>>,
        IAsyncObserver<EdictEvent>,
        IStreamSubscriptionObserver,
        IEdictEventConsumer,
        IRemindable
    where TPayload : new()
{
    OutboxHost<TPayload>? _host;

    /// <summary>
    /// Maximum number of distinct <see cref="EdictEvent.EventId"/>s remembered.
    /// Override in the subclass to tune for expected redelivery volume.
    /// </summary>
    protected virtual int RingSize => 100;

    IdempotencyState Idempotency => base.State.Idempotency;

    private protected OutboxHost<TPayload> Host => _host ??= BuildHost();

    /// <summary>
    /// Test-only probe over the framework-owned Outbox slice. Internal so the
    /// Edict probe grains (table-projection-builder probes) can assert
    /// pending-entry counts; not part of the consumer surface.
    /// </summary>
    internal OutboxSlice OutboxStateForProbe => base.State.Outbox;

    /// <summary>
    /// Drains anything left from a crash before the grain serves traffic
    /// (drain-on-activation, ADR 0018). Steady state has nothing pending so
    /// this is a cheap check.
    /// </summary>
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await Host.OnActivateAsync();
    }

    /// <inheritdoc />
    public Task ReceiveReminder(string reminderName, TickStatus status) =>
        Host.ReceiveReminderAsync();

    /// <summary>
    /// Pure-implicit stream wiring (the trap-free shape of the maintainer's
    /// in-memory-stream guide): the runtime hands one handle per matching
    /// <c>[ImplicitStreamSubscription]</c> and we <see cref="ResumeAsync"/>
    /// against this grain so <see cref="OnNextAsync"/> receives delivery. The
    /// previous hybrid (implicit attribute + explicit <c>SubscribeAsync</c> in
    /// <c>OnActivateAsync</c>) is what stopped memory-stream delivery to
    /// referenced-assembly consumers in #53.
    /// </summary>
    public Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory) =>
        handleFactory.Create<EdictEvent>().ResumeAsync(this);

    /// <inheritdoc />
    public Task OnNextAsync(EdictEvent item, StreamSequenceToken? token = null) =>
        OnStreamEventAsync(item, token);

    /// <inheritdoc />
    public Task OnCompletedAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnErrorAsync(Exception ex) => Task.CompletedTask;

    /// <summary>
    /// In-memory delivery seam (<see cref="IEdictEventConsumer.OnEdictEventAsync"/>):
    /// the Test Framework's in-process stream-provider replacement invokes this
    /// per publish, bypassing the Orleans memory-stream pulling agent that
    /// stops delivering to referenced-assembly consumers (#53). Routes through
    /// the same dedup-guarded callback as Orleans's real delivery so the engine
    /// behaviour is identical under test and in production.
    /// </summary>
    public Task OnEdictEventAsync(EdictEvent evt) => OnStreamEventAsync(evt, null);

    /// <summary>
    /// Implemented by the concrete subclass (or a future generator) to dispatch
    /// the incoming event to a strongly typed handler. Returns <c>true</c> if
    /// the event was handled (ring slot consumed on success), <c>false</c> if
    /// the event type is not handled by this consumer (no ring slot consumed).
    /// A thrown exception leaves the <see cref="EdictEvent.EventId"/> uncommitted so
    /// Orleans redelivers.
    /// </summary>
    protected abstract Task<bool> DispatchAsync(EdictEvent evt);

    /// <summary>
    /// The dedup-guarded stream callback. Invoked by <see cref="OnNextAsync"/>
    /// for every event the runtime delivers via the implicit subscription.
    /// <c>EdictEventHandler</c> overrides this to swap inline dispatch for a
    /// deferred <see cref="OutboxEffectKind.InvokeHandler"/> stage so the
    /// consumer's <c>Handle(TEvent)</c> runs off the stream-callback path with
    /// retry/backoff/dead-letter wrapping (ADR 0023).
    /// </summary>
    protected virtual async Task OnStreamEventAsync(EdictEvent evt, StreamSequenceToken? _)
    {
        EnsureRingInitialized();

        if (Contains(evt.EventId))
        {
            EmitDedupSpan(evt);
            return;
        }

        var handled = await DispatchAsync(evt);

        if (handled)
        {
            Commit(evt.EventId);

            // The ring slot and any outbox effect the subclass staged commit
            // in the SAME one WriteStateAsync (ADR 0018): a Table Projection
            // Builder's row write is an UpsertRow effect atomic with this ring
            // commit, then drained at-least-once — closing the ADR-0012
            // double-apply gap. Plain consumers stage nothing, so the path
            // stays a single ring-only write with no engine/reminder churn.
            var entries = CollectPendingOutboxEntries();
            if (entries.Count == 0)
            {
                await WriteStateAsync();
            }
            else
            {
                await Host.EnqueueAndDrainAsync(entries);
            }
        }
    }

    /// <summary>
    /// Hook for a subclass to contribute durable side-effects staged during
    /// dispatch (the Table Projection Builder's <see cref="OutboxEffectKind.UpsertRow"/>
    /// entry, a saga's <see cref="OutboxEffectKind.SendCommand"/> entry).
    /// Returning a non-empty list routes the ring commit through the Outbox
    /// host so the ring slot and the effect commit atomically in one write.
    /// The default is empty — event handlers and the in-memory projection
    /// builder keep the ring-only commit unchanged.
    /// </summary>
    protected virtual IReadOnlyList<OutboxEntry> CollectPendingOutboxEntries() => [];

    /// <summary>
    /// Called by the generated <c>DispatchAsync</c> for each matched event type.
    /// The default passes the event directly to <paramref name="handler"/>.
    /// <c>EdictTableProjectionBuilder&lt;T&gt;</c> wraps it with
    /// load-apply-writeback (ADR 0012); <c>EdictSaga&lt;TProgress&gt;</c> wraps
    /// it to reset the single outbound-command buffer per event (ADR 0020).
    /// Lives on the shared idempotency root so every consumer role — handler,
    /// projection builder, saga — shares one dispatch seam.
    /// </summary>
    protected virtual Task DispatchEventAsync<TEvent>(TEvent evt, Func<TEvent, Task> handler)
        where TEvent : EdictEvent
        => handler(evt);

    private protected void EnsureRingInitialized()
    {
        if (Idempotency.HandledEventIds.Length != RingSize)
        {
            Idempotency.HandledEventIds = new Guid[RingSize];
            Idempotency.Head = 0;
            Idempotency.Count = 0;
        }
    }

    private protected bool Contains(Guid eventId)
    {
        if (Idempotency.Count < Idempotency.HandledEventIds.Length)
        {
            return Array.IndexOf(Idempotency.HandledEventIds, eventId, 0, Idempotency.Count) >= 0;
        }

        return Array.IndexOf(Idempotency.HandledEventIds, eventId) >= 0;
    }

    private protected void Commit(Guid eventId)
    {
        Idempotency.HandledEventIds[Idempotency.Head] = eventId;
        Idempotency.Head = (Idempotency.Head + 1) % RingSize;

        if (Idempotency.Count < RingSize)
        {
            Idempotency.Count++;
        }
    }

    private protected static void EmitDedupSpan(EdictEvent evt)
    {
        var parentContext = ActivityExtensions.RestoreFromStrings(evt.TraceId, evt.SpanId, evt.TraceState);
        using var span = EdictDiagnostics.ActivitySource.StartEdictEventDeduplicated(evt.GetType().Name, parentContext);
        span?.SetTag("edict.deduplicated", true);
    }

    OutboxHost<TPayload> BuildHost() =>
        new(
            new GrainPersistentStateAdapter<GrainEnvelope<TPayload>>(
                get: () => base.State,
                set: v => base.State = v,
                writeState: WriteStateAsync),
            this.GetStreamProvider("edict"),
            new GrainReminderRegistrar(this),
            ServiceProvider.GetServices<IOutboxEffectExecutor>(),
            ServiceProvider.GetRequiredService<EdictOutboxOptions>(),
            ServiceProvider.GetRequiredService<TimeProvider>(),
            ServiceProvider.GetRequiredService<IDeadLetterPromoter>(),
            grainKey: this.GetPrimaryKey().ToString(),
            grainTypeName: GetType().FullName ?? GetType().Name,
            deferredDispatch: evt => DispatchAsync(evt));
}

/// <summary>
/// Payload-free convenience shim over <see cref="EdictIdempotencyBase{TPayload}"/>
/// closed on <see cref="EdictUnit"/>. Event handlers and projection builders
/// ride this so their consumer-visible signatures never sprout
/// <c>&lt;EdictUnit&gt;</c>; a saga closes the generic base on its progress type.
/// </summary>
public abstract class EdictIdempotencyBase : EdictIdempotencyBase<EdictUnit>;
