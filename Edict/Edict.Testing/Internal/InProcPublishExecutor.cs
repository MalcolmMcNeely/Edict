using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.Idempotency;
using Edict.Core.Outbox;
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
/// timeline and stamps identity / time / trace exactly as the real
/// <c>PublishEventExecutor</c> does. Orchestrates over <see cref="ChaosRoller"/>
/// (per-arrival reorder rolls, per-emission duplicate rolls) and
/// <see cref="HeldQueue"/> (per-subscriber K-counter holds) so every consumer
/// test exercises both the dedup ring and the reorder-tolerance contract.
/// </summary>
sealed class InProcPublishExecutor(
    Serializer serializer,
    IGrainFactory grainFactory,
    SubscriberMap subscribers,
    ChaosOptions chaos,
    TimelineRecorder recorder) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    readonly ChaosRoller _roller = new(chaos);
    readonly HeldQueue _held = new();
    readonly Lock _heldLock = new();

    public Task ExecuteAsync(
        OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent)
    {
        var evt = liveWireEvent ?? serializer.Deserialize<EdictEvent>(entry.Payload);
        var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);

        using var publishActivity = EdictDiagnostics.ActivitySource.StartEdictEventPublish(
            evt.GetType().Name, parentContext);

        var (fallbackTraceId, fallbackSpanId) = SplitTraceParent(entry.TraceParent);

        var stamped = evt with
        {
            EventId = Guid.NewGuid(),
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
            : EventStreamAddress.Resolve(stamped).RouteKey;

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

        return Task.CompletedTask;
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

        foreach (var (key, evt) in flushed)
        {
            var (grainClass, routeKey) = (SubscriberKey)key;
            Dispatch(grainClass, routeKey, evt);
        }

        return Task.FromResult(flushed.Count);
    }

    void Dispatch(Type grainClass, Guid routeKey, EdictEvent evt)
    {
        var grain = grainFactory.GetGrain<IEdictEventConsumer>(routeKey, grainClass.FullName);
        var deliveries = 1 + _roller.ExtraDeliveries(grainClass);
        for (var i = 0; i < deliveries; i++)
        {
            _ = grain.OnEdictEventAsync(evt);
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

    readonly record struct SubscriberKey(Type GrainClass, Guid RouteKey);
}
