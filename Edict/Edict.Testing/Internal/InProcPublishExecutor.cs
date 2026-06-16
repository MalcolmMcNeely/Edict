using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.Idempotency;
using Edict.Core.Outbox;
using Edict.Core.Schedules;
using Edict.Telemetry;

using Orleans;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Testing.Internal;

/// <summary>
/// Replaces the bare <see cref="OutboxEffectKind.PublishEvent"/> executor in
/// the shipped Test Framework. Dispatches the event synchronously to every
/// implicit subscriber via the framework's <see cref="IEdictEventConsumer"/>
/// seam, bypassing the Orleans memory-stream pulling agent that does not
/// deliver to referenced-assembly consumers. Same effect from the engine's
/// point of view (Kind = <see cref="OutboxEffectKind.PublishEvent"/>), so the
/// rest of the Outbox pipeline is unchanged. Also records the event on the
/// timeline and stamps trace context per publish exactly as the real
/// <c>PublishEventExecutor</c> does, carrying the payload's already-assigned
/// <c>EventId</c> unchanged. Orchestrates over <see cref="ChaosRoller"/>
/// (per-arrival reorder rolls, per-emission duplicate rolls) and
/// <see cref="HeldQueue"/> (per-subscriber K-counter holds) so every consumer
/// test exercises both the dedup ring and the reorder-tolerance contract.
/// </summary>
sealed class InProcPublishExecutor(
    Serializer serializer,
    IGrainFactory grainFactory,
    SubscriberMap subscribers,
    ChaosOptions chaos,
    IEventStreamAccessors accessors,
    IEventTagWriters tagWriters,
    TimelineRecorder recorder,
    ConcurrentDictionary<(string GrainClassName, string Key), byte> routedGrains) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    readonly ChaosRoller _roller = new(chaos);
    readonly HeldQueue _held = new();
    readonly Lock _heldLock = new();
    readonly ConcurrentDictionary<SubscriberKey, Task> _deliveryTails = new();
    int _outstandingDispatches;
    ExceptionDispatchInfo? _firstFault;

    public int OutstandingDispatches => Volatile.Read(ref _outstandingDispatches);

    /// <summary>
    /// The first dispatch fault that escaped a fire-and-forget delivery, captured
    /// with its original stack so <c>Drain</c> can rethrow it unwrapped. Null
    /// until a saga or projection <c>HandleAsync</c> throws.
    /// </summary>
    public ExceptionDispatchInfo? FirstFault => Volatile.Read(ref _firstFault);

    public int HeldCount
    {
        get
        {
            lock (_heldLock)
            {
                return _held.Count;
            }
        }
    }

    public Task<OutboxEntry?> ExecuteAsync(
        OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent)
    {
        var edictEvent = liveWireEvent ?? serializer.Deserialize<EdictEvent>(entry.Payload);

        // Mirrors production PublishEventExecutor: a live ref means the publish runs
        // in the same turn that raised the event (inline drain) so the span nests
        // under the staging command; a null ref means the entry was rehydrated in a
        // later drain turn (reminder / activation / dead-letter-raised tail) so the
        // publish is its own root linking back to the staging command.
        using var publishActivity = liveWireEvent is null
            ? EdictDiagnostics.ActivitySource.StartEdictEventPublishLinked(
                edictEvent.GetType().Name, ActivityExtensions.BuildLink(entry.TraceParent, entry.TraceState))
            : EdictDiagnostics.ActivitySource.StartEdictEventPublish(
                edictEvent.GetType().Name, ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState));

        if (publishActivity is not null && tagWriters.TryGet(edictEvent.GetType(), out var write))
        {
            write(edictEvent, publishActivity);
        }

        var (fallbackTraceId, fallbackSpanId) = SplitTraceParent(entry.TraceParent);

        // Matches production: EventId is assigned once as the event enters the
        // Outbox and carried on the payload, never re-minted per publish. Trace
        // context is stamped fresh per publish.
        var stamped = edictEvent with
        {
            TraceId = publishActivity?.TraceId.ToHexString() ?? fallbackTraceId,
            SpanId = publishActivity?.SpanId.ToHexString() ?? fallbackSpanId,
            TraceState = publishActivity?.TraceStateString ?? entry.TraceState,
        };

        recorder.RecordEvent(stamped);

        // The engine's dead-letter promotion path bypasses the InvokeHandler
        // executor on the final attempt — it appends an EdictDeadLetterRaised
        // PublishEvent entry instead. Recording the DeadLettered Invocation
        // entry here closes the loop so the timeline shows the same
        // "event arrived → ran (or dead-lettered)" pair the shipped contract
        // documents.
        if (stamped is EdictDeadLetterRaised raised
            && raised.Kind == nameof(OutboxEffectKind.InvokeHandler)
            && raised.SourceEventType is { } sourceType
            && raised.SourceEventId is { } sourceEventId)
        {
            recorder.RecordInvocation(ShortTypeName(sourceType), sourceEventId, "DeadLettered");
        }

        var routeKey = stamped is EdictEventEnvelope envelope && envelope.InnerEventStreamName is not null
            ? envelope.InnerEventRouteKey
            : accessors.Resolve(stamped).RouteKey;

        // Fire-and-forget per subscriber: a real stream hop is asynchronous to
        // the publishing grain, so a saga reaction that fans back to the
        // same aggregate is free of re-entrant grain-turn deadlock. The
        // harness's Drain settles on recorder-count stability, which captures
        // the full cascade.
        foreach (var grainClass in subscribers.SubscribersFor(stamped))
        {
            var subscriberKey = new SubscriberKey(grainClass, routeKey);
            var (hold, holdDistance) = _roller.ShouldHold(grainClass);

            IReadOnlyList<EdictEvent> readyToEmit;
            lock (_heldLock)
            {
                readyToEmit = _held.OnArrival(subscriberKey, stamped, hold ? holdDistance : 0);
            }

            foreach (var ready in readyToEmit)
            {
                Dispatch(grainClass, routeKey, ready);
            }
        }

        return Task.FromResult<OutboxEntry?>(null);
    }

    /// <summary>
    /// Empties the held queue through the same dispatch path used on arrival,
    /// in original arrival order across subscribers. Duplicate rolls still
    /// apply at emission time. The harness's <c>Drain</c> calls this once the
    /// timeline has gone quiet so reorder-held events get a chance to land
    /// before the test asserts.
    /// </summary>
    public Task<int> FlushHeldAsync()
    {
        IReadOnlyList<(object SubscriberKey, EdictEvent Event)> flushed;
        lock (_heldLock)
        {
            flushed = _held.FlushAll();
        }

        foreach (var (key, edictEvent) in flushed)
        {
            var (grainClass, routeKey) = (SubscriberKey)key;
            Dispatch(grainClass, routeKey, edictEvent);
        }

        return Task.FromResult(flushed.Count);
    }

    void Dispatch(Type grainClass, string routeKey, EdictEvent edictEvent)
    {
        // A saga starts a schedule from inside an event HandleAsync, so it joins
        // the schedule-fire roster the moment an event fans out to it — the
        // command-routed roster (RecordingSender) never sees a saga. Only
        // schedule-capable subscribers are recorded; event handlers and projection
        // builders do not implement the fire seam.
        if (typeof(IEdictScheduleFireable).IsAssignableFrom(grainClass) && grainClass.FullName is { } grainClassName)
        {
            routedGrains.TryAdd((grainClassName, routeKey), 0);
        }

        var grain = grainFactory.GetGrain<IEdictEventConsumer>(routeKey, grainClass.FullName);
        var subscriberKey = new SubscriberKey(grainClass, routeKey);
        var deliveries = 1 + _roller.ExtraDeliveries(grainClass);
        for (var i = 0; i < deliveries; i++)
        {
            Interlocked.Increment(ref _outstandingDispatches);
            // Serialize deliveries to one consumer activation: each awaits the
            // previous delivery to the same (grain class, route key) before it
            // invokes the grain. This mirrors the Orleans pulling agent, which
            // awaits each OnNextAsync before delivering the next item from the
            // same stream to the same consumer — distinct events for one activation
            // are never in flight together, and a redelivery follows the original
            // rather than racing it. Deliveries to different activations stay
            // concurrent, as they are across a real cluster.
            _deliveryTails.AddOrUpdate(
                subscriberKey,
                _ => DeliverAfterAsync(Task.CompletedTask, grain, edictEvent),
                (_, previous) => DeliverAfterAsync(previous, grain, edictEvent));
        }
    }

    async Task DeliverAfterAsync(Task previousDelivery, IEdictEventConsumer grain, EdictEvent edictEvent)
    {
        // Inherit only ordering from the predecessor, never its failure: a faulted
        // earlier delivery has already captured its own fault, so swallow the await
        // and still deliver this event.
        try
        {
            await previousDelivery;
        }
        catch
        {
        }

        try
        {
            // One delivery attempt, mirroring a single pulling-agent hand-off.
            // Serial per-activation delivery means two writes can no longer race
            // one grain's state, so the InconsistentStateException the harness used
            // to retry past no longer arises.
            await grain.OnEdictEventAsync(edictEvent);
        }
        catch (Exception dispatchException)
        {
            // A saga/projection HandleAsync throw propagates out of the grain call
            // here. The harness has no stream to redeliver it, so without capture
            // the fault would escape onto this unobserved task and Drain would
            // settle as if the event were delivered. First writer wins so the
            // earliest fault is the one Drain reports.
            Interlocked.CompareExchange(ref _firstFault, ExceptionDispatchInfo.Capture(dispatchException), null);
        }
        finally
        {
            Interlocked.Decrement(ref _outstandingDispatches);
        }
    }

    static string ShortTypeName(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        return lastDot < 0 ? fullName : fullName[(lastDot + 1)..];
    }

    static (string? TraceId, string? SpanId) SplitTraceParent(string? traceParent)
    {
        if (traceParent is null)
        {
            return (null, null);
        }

        var parts = traceParent.Split('-');
        return parts.Length == 4 ? (parts[1], parts[2]) : (null, null);
    }

    readonly record struct SubscriberKey(Type GrainClass, string RouteKey);
}
