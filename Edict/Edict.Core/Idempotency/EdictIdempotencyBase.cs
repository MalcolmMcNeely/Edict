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
    : Grain<GrainEnvelope<IdempotencyPayload<TPayload>>>, IEdictDeadLetterAdmin
    where TPayload : new()
{
    /// <summary>
    /// Maximum number of distinct <see cref="EdictEvent.EventId"/>s remembered.
    /// Override in the subclass to tune for expected redelivery volume.
    /// </summary>
    protected virtual int RingSize => 100;

    IdempotencyState Ring => State.Payload.Ring;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await SubscribeToStreamAsync(cancellationToken);
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
            await WriteStateAsync();
        }
    }

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
