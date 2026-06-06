using System.Diagnostics;

using Edict.Contracts;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.ClaimCheck;
using Edict.Core.DeadLetter;
using Edict.Core.Metrics;
using Edict.Core.Outbox;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Serialization;
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
/// subclass's dispatch succeeds. All outbox plumbing — drain
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
/// shares the same atomic write.
/// </para>
/// <para>
/// Receiver-side bifurcation: the stream-observer callback splits on the
/// wire-frame's claim-check shape. Non-envelopes and inline-payload
/// envelopes flow through <see cref="OnStreamEventAsync"/> inline —
/// ring-equals-row atomicity is preserved for the common case.
/// Pointer-bearing envelopes commit the ring slot for the envelope's
/// wire-frame <see cref="EdictEvent.EventId"/> and stage an
/// <see cref="OutboxEffectKind.InvokeHandler"/> entry in one atomic write; the
/// engine takes over from there (fetch blob → dispatch via the
/// deferred-dispatch callback), inheriting per-entry retry/backoff and
/// <see cref="IDeadLetterPromoter"/> exhaustion semantics on the same surface
/// the publisher-side path uses.
/// </para>
/// </summary>
[StorageProvider(ProviderName = "edict-state")]
public abstract class EdictIdempotencyBase<TPayload>
    : Grain<GrainEnvelope<TPayload>>,
        IAsyncObserver<EdictEvent>,
        IStreamSubscriptionObserver,
        IEdictEventConsumer,
        IRemindable
    where TPayload : IEdictPersistedState, new()
{
    OutboxHost<TPayload>? _host;
    ClaimCheckUnwrap? _unwrap;
    int? _cachedWindowSize;
    DedupRingMirror? _dedupMirror;
    Guid[]? _mirroredRing;
    Serializer? _cachedSerializer;

    /// <summary>
    /// Maximum number of distinct <see cref="EdictEvent.EventId"/>s remembered
    /// in the dedup window. The silo-wide default comes from
    /// <see cref="EdictOptions.IdempotencyWindowSize"/>; override in a specific
    /// subclass (e.g. a high-throughput singleton consumer) to use a different
    /// window for that grain type. Resolved once per activation and cached —
    /// the dedup ring runs on the per-event hot path, so a DI lookup per
    /// event is wasted work.
    /// </summary>
    protected virtual int WindowSize =>
        _cachedWindowSize ??= ServiceProvider.GetService<IOptions<EdictOptions>>()?.Value.IdempotencyWindowSize
            ?? new EdictOptions().IdempotencyWindowSize;

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
    /// (drain-on-activation). Steady state has nothing pending so
    /// this is a cheap check.
    /// </summary>
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await Host.OnActivateAsync();
    }

    /// <summary>
    /// Removes this consumer's entry from the silo-local metrics cache so a
    /// deactivated grain stops contributing to
    /// <c>edict.outbox.pending.count</c> / <c>edict.outbox.oldest_entry.age</c>
    /// / <c>edict.saga.progress.age</c>. Without this hook a grain that
    /// deactivates with pending work would contribute its last reported depth
    /// to the per-type aggregate forever.
    /// </summary>
    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        await Host.OnDeactivateAsync();
        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task ReceiveReminder(string reminderName, TickStatus status) =>
        Host.ReceiveReminderAsync();

    /// <summary>
    /// Pure-implicit stream wiring (the trap-free shape of the maintainer's
    /// in-memory-stream guide): the runtime hands one handle per matching
    /// <c>[ImplicitStreamSubscription]</c> and we <see cref="ResumeAsync"/>
    /// against this grain so <see cref="OnNextAsync"/> receives delivery.
    /// </summary>
    public Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory) =>
        handleFactory.Create<EdictEvent>().ResumeAsync(this);

    /// <inheritdoc />
    public Task OnNextAsync(EdictEvent item, StreamSequenceToken? token = null) =>
        UnwrapAndDispatchAsync(item, token);

    /// <inheritdoc />
    public Task OnCompletedAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnErrorAsync(Exception exception) => Task.CompletedTask;

    /// <summary>
    /// In-memory delivery seam (<see cref="IEdictEventConsumer.OnEdictEventAsync"/>):
    /// the Test Framework's in-process stream-provider replacement invokes this
    /// per publish, bypassing the Orleans memory-stream pulling agent that
    /// stops delivering to referenced-assembly consumers. Routes through
    /// the same bifurcation as Orleans's real delivery so the engine behaviour
    /// is identical under test and in production.
    /// </summary>
    public Task OnEdictEventAsync(EdictEvent edictEvent) => UnwrapAndDispatchAsync(edictEvent, null);

    /// <summary>
    /// Receiver-side bifurcation: non-envelope payloads and inline-payload
    /// envelopes dispatch inline through <see cref="OnStreamEventAsync"/>
    /// (ring check → DispatchAsync → ring commit + any staged effects
    /// atomic, ring-equals-row preserved). Pointer-bearing envelopes
    /// commit the ring slot for the
    /// envelope's wire-frame <see cref="EdictEvent.EventId"/> and stage an
    /// <see cref="OutboxEffectKind.InvokeHandler"/> entry in one atomic write;
    /// the engine's per-entry retry takes the fetch-and-dispatch from there.
    /// </summary>
    async Task UnwrapAndDispatchAsync(EdictEvent incoming, StreamSequenceToken? token)
    {
        if (incoming is EdictEventEnvelope { InlinePayload: null } envelope)
        {
            await StagePointerEnvelopeForDeferredDispatchAsync(envelope);
            return;
        }

        var unwrap = _unwrap ??= ServiceProvider.GetRequiredService<ClaimCheckUnwrap>();
        // The inline branch only ever sees non-pointer frames (pointer envelopes
        // are staged for deferred dispatch above), so no GET span fires here and
        // there is no consumer-turn span to parent it under yet.
        var materialised = await unwrap.ApplyAsync(incoming, GetType(), default, CancellationToken.None);
        await OnStreamEventAsync(materialised, token);
    }

    /// <summary>
    /// Pointer-envelope intake: commits the ring slot for the envelope's
    /// wire-frame <see cref="EdictEvent.EventId"/> and stages an
    /// <see cref="OutboxEffectKind.InvokeHandler"/> entry carrying the envelope
    /// itself as its payload, in one atomic write. The engine's per-entry
    /// retry runs the fetch via <see cref="ClaimCheckUnwrap"/> inside
    /// <c>InvokeHandlerExecutor</c>; on <see cref="EdictOptions.OutboxMaxAttempts"/>
    /// exhaustion the standard dead-letter promotion synthesises an
    /// <c>EdictDeadLetterRaised</c> with the <c>BlobMissing</c> failure kind,
    /// the envelope's <see cref="EdictEvent.EventId"/> locating the parked body.
    /// </summary>
    private protected virtual async Task StagePointerEnvelopeForDeferredDispatchAsync(EdictEventEnvelope envelope)
    {
        EnsureWindowInitialized();

        if (IsDeferredRedelivery(envelope))
        {
            return;
        }

        await CommitAndPersistAsync(envelope.EventId, BuildInvokeHandlerEntry(envelope));
    }

    /// <summary>
    /// Dedup check for the pointer-envelope path: an already-handled wire-frame
    /// <see cref="EdictEvent.EventId"/> is a redelivery — emit the dedup span and
    /// metric and report it so the caller can suppress it before staging.
    /// </summary>
    private protected bool IsDeferredRedelivery(EdictEventEnvelope envelope)
    {
        if (!Contains(envelope.EventId))
        {
            return false;
        }

        EmitDedupSpan(envelope);
        IdempotencyDedupMetrics.EmitDedupHit(envelope, GetType().FullName ?? GetType().Name);
        return true;
    }

    /// <summary>
    /// Builds the <see cref="OutboxEffectKind.InvokeHandler"/> entry that carries
    /// the pointer envelope as its payload, preferring the envelope's embedded
    /// trace ids (stamped by <c>PublishEventExecutor</c>) over
    /// <see cref="Activity.Current"/> — Azure Queue streams do not propagate
    /// <see cref="Activity.Current"/> across the hop, but the publish span's
    /// identity rides on the event itself so the deferred handle span still nests
    /// as parent-child.
    /// </summary>
    private protected OutboxEntry BuildInvokeHandlerEntry(EdictEventEnvelope envelope)
    {
        var serializer = _cachedSerializer ??= ServiceProvider.GetRequiredService<Serializer>();

        string? traceParent;
        string? traceState;
        if (envelope.TraceId is { Length: 32 } eventTraceId && envelope.SpanId is { Length: 16 } eventSpanId)
        {
            traceParent = ActivityExtensions.BuildTraceParent(eventTraceId, eventSpanId);
            traceState = envelope.TraceState;
        }
        else if (Activity.Current is { } current)
        {
            traceParent = current.BuildTraceParent();
            traceState = current.TraceStateString;
        }
        else
        {
            traceParent = null;
            traceState = null;
        }

        return new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = serializer.SerializeToArray<EdictEvent>(envelope),
            TraceParent = traceParent,
            TraceState = traceState,
        };
    }

    /// <summary>
    /// Implemented by the generated spine to dispatch the incoming event to a
    /// strongly typed handler. The returned <see cref="EdictDispatchOutcome"/>
    /// reports whether the type matched a handler arm (ring slot consumed on
    /// success) and carries the single downstream effect a saga or
    /// table-projection builder staged during the handler. A thrown exception
    /// leaves the <see cref="EdictEvent.EventId"/> uncommitted so Orleans
    /// redelivers.
    /// </summary>
    protected abstract Task<EdictDispatchOutcome> DispatchAsync(EdictEvent edictEvent);

    /// <summary>
    /// The deferred-dispatch callback the Outbox engine invokes when it drains an
    /// <see cref="OutboxEffectKind.InvokeHandler"/> entry: runs the handler on the
    /// already-materialised event and returns the single downstream effect it
    /// staged. <c>EdictSaga</c> overrides it to fold in the lifecycle a
    /// <c>Complete()</c> implies, so terminalisation rides the engine's ack-write
    /// for the entry rather than being lost on this off-stream path.
    /// </summary>
    private protected virtual async Task<OutboxEntry?> DispatchDeferredAsync(EdictEvent edictEvent) =>
        (await DispatchAsync(edictEvent)).StagedEffect;

    /// <summary>
    /// The dedup-guarded stream callback. Invoked by the bifurcation for the
    /// non-envelope / inline-payload-envelope branch; the
    /// pointer-envelope branch bypasses this in favour of an
    /// <see cref="OutboxEffectKind.InvokeHandler"/> entry the engine drains.
    /// <c>EdictEventHandler</c> overrides this to swap inline dispatch for a
    /// deferred <see cref="OutboxEffectKind.InvokeHandler"/> stage so the
    /// consumer's <c>HandleAsync(TEvent)</c> runs off the stream-callback path with
    /// retry/backoff/dead-letter wrapping.
    /// </summary>
    protected virtual async Task OnStreamEventAsync(EdictEvent edictEvent, StreamSequenceToken? _)
    {
        EnsureWindowInitialized();

        if (Contains(edictEvent.EventId))
        {
            EmitDedupSpan(edictEvent);
            IdempotencyDedupMetrics.EmitDedupHit(edictEvent, GetType().FullName ?? GetType().Name);
            return;
        }

        var outcome = await DispatchAsync(edictEvent);

        if (outcome.Handled)
        {
            // The ring slot and any outbox effect the dispatch staged commit in
            // the SAME one write — a List Projection Builder's row write is an
            // UpsertRow effect atomic with this ring commit, then drained
            // at-least-once. Plain consumers stage nothing, so the path stays a
            // single ring-only write with no engine/reminder churn. The
            // dedup-window mirror is confirmed only after that write lands, so a
            // write fault re-dispatches the redelivery instead of suppressing it.
            await CommitAndPersistAsync(edictEvent.EventId, outcome.StagedEffect);
        }
    }

    /// <summary>
    /// Called by the generated <c>DispatchAsync</c> for each matched event type.
    /// The default passes the event directly to <paramref name="handler"/> and
    /// stages no effect. <c>EdictListProjectionBuilder&lt;TRow&gt;</c> wraps it
    /// with load-apply-writeback and returns an
    /// <see cref="OutboxEffectKind.UpsertRow"/> effect;
    /// <c>EdictSaga&lt;TProgress&gt;</c> wraps it to buffer the single outbound
    /// command and returns a <see cref="OutboxEffectKind.SendCommand"/> effect.
    /// The effect rides the return value — never a grain field — so a parallel
    /// deferred drain cannot lose or cross-wire it. Lives on the shared
    /// idempotency root so every consumer role shares one dispatch seam.
    /// </summary>
    protected virtual async Task<EdictDispatchOutcome> DispatchEventAsync<TEvent>(TEvent edictEvent, Func<TEvent, Task> handler)
        where TEvent : EdictEvent
    {
        await handler(edictEvent);
        return EdictDispatchOutcome.HandledWithNoEffect;
    }

    private protected void EnsureWindowInitialized()
    {
        if (Idempotency.HandledEventIds.Length != WindowSize)
        {
            Idempotency.HandledEventIds = new Guid[WindowSize];
            Idempotency.Head = 0;
            Idempotency.Count = 0;
        }

        // The mirror is in-memory only and must be rebuilt from the canonical
        // persisted ring on activation, or whenever the ring reference is
        // swapped (e.g. WindowSize changed). Steady state hits the
        // reference-equal early-out.
        if (_dedupMirror is null || !ReferenceEquals(_mirroredRing, Idempotency.HandledEventIds))
        {
            _dedupMirror ??= new DedupRingMirror();
            _dedupMirror.Activate(Idempotency.HandledEventIds, Idempotency.Head, Idempotency.Count);
            _mirroredRing = Idempotency.HandledEventIds;
        }
    }

    private protected bool Contains(Guid eventId) => _dedupMirror!.Contains(eventId);

    /// <summary>
    /// Commits the dedup-window slot for <paramref name="eventId"/> and any
    /// staged effect through the host's atomic-write boundary. The persisted ring
    /// slot and the in-memory mirror are advanced together before the write, so a
    /// concurrent redelivery of the same in-flight event is suppressed; a write
    /// fault rolls the ring back and rebuilds the mirror from it, so once the
    /// failure is durable a genuine redelivery is re-dispatched rather than
    /// suppressed against an id the store never persisted — at-least-once is
    /// preserved.
    /// </summary>
    private protected Task CommitAndPersistAsync(Guid eventId, OutboxEntry? stagedEffect)
    {
        DedupRing.Revert revert = default;
        return Host.CommitProgressAndDrainAsync(
            applyProgress: () =>
            {
                revert = DedupRing.Apply(Idempotency, WindowSize, eventId);
                _dedupMirror!.Commit(eventId);
                ApplyConsumerProgress();
            },
            rollbackProgress: () =>
            {
                RollbackConsumerProgress();
                DedupRing.RollBack(Idempotency, revert);
                _dedupMirror!.Activate(Idempotency.HandledEventIds, Idempotency.Head, Idempotency.Count);
            },
            stagedEffect: stagedEffect,
            onDrained: MarkConsumerProgressDrained);
    }

    /// <summary>
    /// Folds a role-specific in-memory state mutation into the same atomic
    /// commit as the dedup-ring slot. Default no-op; <c>EdictSaga</c> overrides
    /// it to arm or terminalise its lifecycle slot so the lifecycle write, the
    /// ring slot, and any staged effect land in one grain-state write. Runs
    /// synchronously inside the host's commit boundary, after the ring slot is
    /// advanced and before the write.
    /// </summary>
    private protected virtual void ApplyConsumerProgress()
    {
    }

    /// <summary>Restores what <see cref="ApplyConsumerProgress"/> mutated when the commit write faults.</summary>
    private protected virtual void RollbackConsumerProgress()
    {
    }

    /// <summary>
    /// Runs after the staged effect has drained — the row write has landed in the
    /// store. Default no-op; a Projection overrides it to advance the in-memory
    /// read-your-writes marker and signal parked readers, so a
    /// <c>CursorReached</c> answer implies the row is readable. The host calls this
    /// only when the effect actually drained (not on transient backoff), so it
    /// never fires ahead of the row landing.
    /// </summary>
    private protected virtual void MarkConsumerProgressDrained()
    {
    }

    private protected static void EmitDedupSpan(EdictEvent edictEvent)
    {
        var link = ActivityExtensions.BuildLink(edictEvent.TraceId, edictEvent.SpanId, edictEvent.TraceState);
        using var span = EdictDiagnostics.ActivitySource.StartEdictEventDeduplicated(edictEvent.GetType().Name, link);
        span?.SetTag(SemanticConventions.Events.Tags.Deduplicated, true);
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
            ServiceProvider.GetRequiredService<IOptions<EdictOptions>>().Value,
            ServiceProvider.GetRequiredService<TimeProvider>(),
            ServiceProvider.GetRequiredService<IDeadLetterPromoter>(),
            grainKey: this.GetPrimaryKey().ToString(),
            grainTypeName: GetType().FullName ?? GetType().Name,
            deferredDispatch: DispatchDeferredAsync,
            consumerType: GetType(),
            metricsCache: ServiceProvider.GetService<IEdictMetricsCache>(),
            requestDeactivation: DeactivateOnIdle);
}

/// <summary>
/// Payload-free convenience shim over <see cref="EdictIdempotencyBase{TPayload}"/>
/// closed on <see cref="EdictUnit"/>. Event handlers and projection builders
/// ride this so their consumer-visible signatures never sprout
/// <c>&lt;EdictUnit&gt;</c>; a saga closes the generic base on its progress type.
/// </summary>
public abstract class EdictIdempotencyBase : EdictIdempotencyBase<EdictUnit>;
