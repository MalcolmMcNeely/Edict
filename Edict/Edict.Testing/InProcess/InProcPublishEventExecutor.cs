using System.Diagnostics;

using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.Idempotency;
using Edict.Core.Outbox;
using Edict.Telemetry;
using Edict.Testing.Chaos;
using Edict.Testing.Recording;

using Orleans;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Testing.InProcess;

/// <summary>
/// Replaces the bare <see cref="OutboxEffectKind.PublishEvent"/> executor in
/// the shipped Test Framework. Dispatches the event synchronously to every
/// implicit subscriber via the framework's <see cref="IEdictEventConsumer"/>
/// seam, bypassing the Orleans memory-stream pulling agent that does not
/// deliver to referenced-assembly consumers in #53. Same effect from the
/// engine's point of view (Kind = <see cref="OutboxEffectKind.PublishEvent"/>),
/// so the rest of the Outbox pipeline is unchanged.
/// <para>
/// Also records the event on the timeline (the choke point the deprecated
/// <c>RecordingPublishEventExecutor</c> used to wrap), and stamps identity /
/// time / trace exactly as the real <see cref="PublishEventExecutor"/> does
/// so a consumer's <see cref="EdictEvent.EventId"/> / <c>OccurredAt</c>
/// expectations are identical under test and in production.
/// </para>
/// </summary>
sealed class InProcPublishEventExecutor(
    Serializer serializer,
    IGrainFactory grainFactory,
    InProcImplicitSubscriberMap subscribers,
    ChaosOptions chaos,
    EdictTimelineRecorder recorder) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    // One Random per executor (per silo), seeded by the chaos policy so the
    // delivery pattern reproduces run-to-run. Single-threaded access is rare
    // (the engine drains FIFO per grain), but a Lock keeps it correct under
    // concurrent grains.
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
        // PublishEvent entry instead. Recording the
        // DeadLettered Invocation entry here closes the loop so the timeline
        // shows the same "event arrived → ran (or dead-lettered)" pair the
        // shipped contract documents.
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
        // the publishing grain, so a saga reaction that fans back out to the
        // SAME aggregate (e.g. saga's ConfirmOrderCommand re-entering the
        // OrderCommandHandler that just raised OrderSubmittedEvent) is free of
        // re-entrant grain-turn deadlock. The harness's Drain settles on
        // recorder-count stability, which captures the full cascade.
        foreach (var grainClass in subscribers.SubscribersFor(stamped))
        {
            var grain = grainFactory.GetGrain<IEdictEventConsumer>(routeKey, grainClass.FullName);
            // Per-subscriber chaos gate: EdictEventHandler deliveries are
            // excluded from chaos extra deliveries by default so a
            // consumer's mock-call-count assertions are deterministic
            // before they have reasoned about chaos.
            var deliveries = 1 + ExtraDeliveriesFor(grainClass);
            for (var i = 0; i < deliveries; i++)
            {
                _ = grain.OnEdictEventAsync(stamped);
            }
        }

        return Task.CompletedTask;
    }

    // Seeded duplicate redelivery (#52 chaos AC): production streams redeliver,
    // so every consumer test exercises the dedup ring for free. The
    // dedup ring suppresses the duplicate, so saga progress / projection rows /
    // recorder counts stay stable across runs.
    int ExtraDeliveriesFor(Type grainClass)
    {
        if (!chaos.Enabled || chaos.MaxExtraDeliveries <= 0)
        {
            return 0;
        }

        if (InProcImplicitSubscriberMap.IsEventHandler(grainClass) && !chaos.InvocationsEnabled)
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
