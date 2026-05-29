using System.Diagnostics;

using Edict.Contracts.Events;
using Edict.Telemetry;

using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Outbox;

sealed class PublishEventExecutor(Serializer serializer, IEventStreamAccessors accessors, IEventTagWriters tagWriters) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    public async Task ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task>? deferredDispatch,
        Type? consumerType,
        EdictEvent? liveWireEvent)
    {
        // Inline drain after Raise hands us the live reference — skip the
        // deserialise. Reminder / activation drains have no live ref and pay
        // the deserialise to rehydrate from the durable payload.
        var edictEvent = liveWireEvent ?? serializer.Deserialize<EdictEvent>(entry.Payload);
        var (streamName, routeKey) = ResolveStreamAddress(edictEvent);
        var stream = streamProvider.GetStream<EdictEvent>(StreamId.Create(streamName, routeKey));

        var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);

        using var publishActivity = EdictDiagnostics.ActivitySource.StartEdictEventPublish(
            edictEvent.GetType().Name, parentContext);

        if (publishActivity is not null && tagWriters.TryGet(edictEvent.GetType(), out var write))
        {
            write(edictEvent, publishActivity);
        }

        var stamped = Stamp(edictEvent, entry, publishActivity);

        await stream.OnNextAsync(stamped);
    }

    public (string StreamName, Guid RouteKey, EdictEvent? ResolvedEvent)? TryResolveBatchKey(
        OutboxEntry entry, EdictEvent? liveWireEvent)
    {
        var edictEvent = liveWireEvent ?? serializer.Deserialize<EdictEvent>(entry.Payload);
        var (streamName, routeKey) = ResolveStreamAddress(edictEvent);
        return (streamName, routeKey, edictEvent);
    }

    public async Task ExecuteBatchAsync(
        IReadOnlyList<OutboxEntry> entries,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task>? deferredDispatch,
        Type? consumerType,
        IReadOnlyList<EdictEvent?> liveWireEvents)
    {
        // All entries in a group share the same (streamName, routeKey) by
        // invariant of the grouping function; resolve once from entry 0.
        var first = liveWireEvents[0] ?? serializer.Deserialize<EdictEvent>(entries[0].Payload);
        var (streamName, routeKey) = ResolveStreamAddress(first);
        var stream = streamProvider.GetStream<EdictEvent>(StreamId.Create(streamName, routeKey));

        // One publish span per event so each event's TraceId/SpanId points at
        // its own span — same observability shape as the per-event path. The
        // activities are held open across the OnNextBatchAsync so the wire
        // send sits inside each event's span.
        var activities = new Activity?[entries.Count];
        var stamped = new EdictEvent[entries.Count];

        try
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var edictEvent = liveWireEvents[i] ?? serializer.Deserialize<EdictEvent>(entry.Payload);
                var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);
                activities[i] = EdictDiagnostics.ActivitySource.StartEdictEventPublish(
                    edictEvent.GetType().Name, parentContext);
                if (activities[i] is { } a && tagWriters.TryGet(edictEvent.GetType(), out var write))
                {
                    write(edictEvent, a);
                }
                stamped[i] = Stamp(edictEvent, entry, activities[i]);
            }

            await stream.OnNextBatchAsync(stamped);
        }
        finally
        {
            for (var i = 0; i < activities.Length; i++)
            {
                activities[i]?.Dispose();
            }
        }
    }

    static EdictEvent Stamp(EdictEvent edictEvent, OutboxEntry entry, Activity? publishActivity)
    {
        // Fall back to the entry's captured ids (null when the command ran with
        // no trace) — never a synthesised all-zero trace id, which a consumer's
        // ActivityTraceId.CreateFromString rejects.
        var (fallbackTraceId, fallbackSpanId) = SplitTraceParent(entry.TraceParent);

        return edictEvent with
        {
            EventId = Guid.NewGuid(),
            TraceId = publishActivity?.TraceId.ToHexString() ?? fallbackTraceId,
            SpanId = publishActivity?.SpanId.ToHexString() ?? fallbackSpanId,
            TraceState = publishActivity?.TraceStateString ?? entry.TraceState,
        };
    }

    // A claim-checked event rides as an EdictEventEnvelope whose inner-event
    // address fields name the domain stream the unwrapped event would have
    // ridden. The envelope itself carries no [EdictStream] because the
    // stream choice is data, not metadata. The receiver-side unwrap picks
    // the envelope off this stream and rehydrates the inner event.
    (string StreamName, Guid RouteKey) ResolveStreamAddress(EdictEvent edictEvent) =>
        edictEvent is EdictEventEnvelope envelope && envelope.InnerEventStreamName is { } streamName
            ? (streamName, envelope.InnerEventRouteKey)
            : accessors.Resolve(edictEvent);

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
