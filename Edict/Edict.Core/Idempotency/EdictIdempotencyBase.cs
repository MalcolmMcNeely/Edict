using Edict.Contracts;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core;
using Edict.Core.Administration;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Idempotency;

/// <summary>
/// Abstract generic base for every event-consuming grain (event handlers,
/// projection builders, sagas — the shared inheritance root, brand-rule clause
/// (b)). Owns the stream-observer callback, suppresses at-least-once
/// redeliveries via a configurable bounded ring of recently seen
/// <see cref="EdictEvent.EventId"/>s, and commits progress only after the
/// subclass's dispatch succeeds (ADR 0002). The persisted document is the
/// single-write <see cref="GrainEnvelope{TPayload}"/> over
/// <see cref="IdempotencyPayload{TPayload}"/> <c>{ Ring, TPayload }</c>, so the
/// dedup ring, consumer payload, and Outbox/DeadLetter slice all commit
/// atomically in one write (ADR 0018).
/// </summary>
[StorageProvider(ProviderName = "edict-dedup")]
public abstract class EdictIdempotencyBase<TPayload>
    : Grain<GrainEnvelope<IdempotencyPayload<TPayload>>>, IEdictDeadLetterAdmin, IRemindable, IOutboxHost
    where TPayload : new()
{
    const string DrainReminderName = "edict-outbox-drain";

    bool _drainReminderRegistered;

    /// <summary>
    /// Maximum number of distinct <see cref="EdictEvent.EventId"/>s remembered.
    /// Override in the subclass to tune for expected redelivery volume.
    /// </summary>
    protected virtual int RingSize => 100;

    IdempotencyState Ring => State.Payload.Ring;

    OutboxDrainEngine Engine => ServiceProvider.GetRequiredService<OutboxDrainEngine>();

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await SubscribeToStreamAsync(cancellationToken);

        // Drain-on-activation (ADR 0018): recover anything a crash left between
        // the ring/outbox commit and the drain — the durable half of the
        // ADR-0012 gap closure. Steady state has nothing pending, so this is a
        // cheap check.
        if (State.Outbox.Pending.Count > 0)
        {
            await Engine.DrainAsync(this);
        }
    }

    /// <summary>
    /// Implemented by the concrete subclass to subscribe to its domain stream,
    /// passing <see cref="OnStreamEventAsync"/> as the callback. The base never
    /// decides which stream or provider to use.
    /// </summary>
    protected abstract Task SubscribeToStreamAsync(CancellationToken cancellationToken);

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
    /// The dedup-guarded stream callback. Subclasses pass this method to
    /// <c>stream.SubscribeAsync</c> from <see cref="SubscribeToStreamAsync"/>.
    /// </summary>
    protected async Task OnStreamEventAsync(EdictEvent evt, StreamSequenceToken _)
    {
        // Block-intake (ADR 0019): a saturated DeadLetter slice must not
        // silently drop a redelivered event. Throw before the dedup check so
        // the EventId is never committed to the ring — Orleans redelivers it
        // until an operator redrives and the cap clears.
        var outboxOptions = ServiceProvider.GetRequiredService<EdictOutboxOptions>();
        if (State.Outbox.IsIntakeBlocked(outboxOptions.DeadLetterCap))
        {
            throw new EdictOutboxSaturatedException();
        }

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
                await Engine.EnqueueAndDrainAsync(this, entries);
            }
        }
    }

    /// <summary>
    /// Hook for a subclass to contribute durable side-effects staged during
    /// dispatch (the Table Projection Builder's <see cref="OutboxEffectKind.UpsertRow"/>
    /// entry, a saga's <see cref="OutboxEffectKind.SendCommand"/> entry).
    /// Returning a non-empty list routes the ring commit through the Outbox
    /// engine so the ring slot and the effect commit atomically in one write.
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

    /// <summary>
    /// Operator recovery (ADR 0019): atomically moves the dead-lettered entry
    /// back to the Outbox tail with <c>AttemptCount</c> reset. The same one
    /// grain-state write clears the cap, so a previously blocked consumer
    /// resumes acking redelivered events.
    /// </summary>
    async Task IEdictDeadLetterAdmin.RedriveAsync(Guid entryId)
    {
        var clock = ServiceProvider.GetRequiredService<TimeProvider>();
        State.Outbox = State.Outbox.Redrive(entryId, clock.GetUtcNow());
        await WriteStateAsync();
    }

    /// <inheritdoc />
    Task<IReadOnlyList<EdictDeadLetterEntry>> IEdictDeadLetterAdmin.ListDeadLetterAsync() =>
        Task.FromResult(DeadLetterProjection.From(State.Outbox));

    /// <inheritdoc />
    public Task ReceiveReminder(string reminderName, TickStatus status)
    {
        // A tick proves a reminder exists; record that so the post-drain
        // reconcile authoritatively unregisters it once the Outbox is empty.
        _drainReminderRegistered = true;
        return Engine.DrainAsync(this);
    }

    // IOutboxHost — implemented explicitly so the engine seam stays off the
    // consumer surface (mirrors EdictCommandHandler<TState>); the grain owns
    // the single WriteStateAsync, the stream provider, and the lazy Reminder.

    OutboxSlice IOutboxHost.Outbox
    {
        get => State.Outbox;
        set => State.Outbox = value;
    }

    IStreamProvider IOutboxHost.StreamProvider => this.GetStreamProvider("edict");

    Task IOutboxHost.CommitAsync() => WriteStateAsync();

    async Task IOutboxHost.RegisterDrainReminderAsync()
    {
        await this.RegisterOrUpdateReminder(
            DrainReminderName, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        _drainReminderRegistered = true;
    }

    async Task IOutboxHost.UnregisterDrainReminderAsync()
    {
        if (!_drainReminderRegistered)
        {
            return; // never registered — keep the happy path off the reminder subsystem
        }

        var reminder = await this.GetReminder(DrainReminderName);
        if (reminder is not null)
        {
            await this.UnregisterReminder(reminder);
        }

        _drainReminderRegistered = false;
    }

    void EnsureRingInitialized()
    {
        if (Ring.Ring.Length != RingSize)
        {
            Ring.Ring = new Guid[RingSize];
            Ring.Head = 0;
            Ring.Count = 0;
        }
    }

    bool Contains(Guid eventId)
    {
        if (Ring.Count < Ring.Ring.Length)
        {
            return Array.IndexOf(Ring.Ring, eventId, 0, Ring.Count) >= 0;
        }

        return Array.IndexOf(Ring.Ring, eventId) >= 0;
    }

    void Commit(Guid eventId)
    {
        Ring.Ring[Ring.Head] = eventId;
        Ring.Head = (Ring.Head + 1) % RingSize;

        if (Ring.Count < RingSize)
        {
            Ring.Count++;
        }
    }

    static void EmitDedupSpan(EdictEvent evt)
    {
        var parentContext = ActivityExtensions.RestoreFromStrings(evt.TraceId, evt.SpanId, evt.TraceState);
        using var span = EdictDiagnostics.ActivitySource.StartEdictEventDeduplicated(evt.GetType().Name, parentContext);
        span?.SetTag("edict.deduplicated", true);
    }
}

/// <summary>
/// Payload-free convenience shim over <see cref="EdictIdempotencyBase{TPayload}"/>
/// closed on <see cref="EdictUnit"/>. Event handlers and projection builders
/// ride this so their consumer-visible signatures never sprout
/// <c>&lt;EdictUnit&gt;</c>; a saga closes the generic base on its progress type.
/// </summary>
public abstract class EdictIdempotencyBase : EdictIdempotencyBase<EdictUnit>;
