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
/// <see cref="PublishEventExecutor"/> does.
/// </summary>
sealed class InProcPublishExecutor(
    Serializer serializer,
    IGrainFactory grainFactory,
    SubscriberMap subscribers,
    ChaosOptions chaos,
    TimelineRecorder recorder) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    readonly Random _chaosRng = new(chaos.Seed);
    readonly Lock _chaosLock = new();

    public Task ExecuteAsync(
        OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch, Type? consumerType)
    {
        var evt = serializer.Deserialize<EdictEvent>(entry.Payload);
        var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);

        using var publishActivity = EdictDiagnostics.ActivitySource.StartEdictEventPublish(
            evt.GetType().Name, parentContext);

        var (fallbackTraceId, fallbackSpanId) = SplitTraceParent(entry.TraceParent);

        var stamped = evt with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
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
            var grain = grainFactory.GetGrain<IEdictEventConsumer>(routeKey, grainClass.FullName);
            var deliveries = 1 + ExtraDeliveriesFor(grainClass);
            for (var i = 0; i < deliveries; i++)
            {
                _ = grain.OnEdictEventAsync(stamped);
            }
        }

        return Task.CompletedTask;
    }

    // Seeded duplicate redelivery: production streams redeliver, so every
    // consumer test exercises the dedup ring for free. The dedup ring
    // suppresses the duplicate, so saga progress / projection rows / recorder
    // counts stay stable across runs.
    int ExtraDeliveriesFor(Type grainClass)
    {
        if (chaos.MaxExtraDeliveries <= 0)
        {
            return 0;
        }

        if (SubscriberMap.IsEventHandler(grainClass) && !chaos.InvocationsEnabled)
        {
            return 0;
        }

        lock (_chaosLock)
        {
            return _chaosRng.NextDouble() < chaos.DuplicateProbability
                ? _chaosRng.Next(1, chaos.MaxExtraDeliveries + 1)
                : 0;
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
}
