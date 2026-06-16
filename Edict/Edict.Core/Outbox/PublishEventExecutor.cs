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

    public async Task<OutboxEntry?> ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch,
        Type? consumerType,
        EdictEvent? liveWireEvent)
    {
        // Inline drain after Raise hands us the live reference — skip the
        // deserialise. Reminder / activation drains have no live ref and pay
        // the deserialise to rehydrate from the durable payload.
        var edictEvent = liveWireEvent ?? serializer.Deserialize<EdictEvent>(entry.Payload);
        var (streamName, routeKey) = ResolveStreamAddress(edictEvent);
        var stream = streamProvider.GetStream<EdictEvent>(StreamId.Create(streamName, routeKey));

        using var publishActivity = StartPublishActivity(edictEvent.GetType().Name, entry, liveWireEvent);

        if (publishActivity is not null && tagWriters.TryGet(edictEvent.GetType(), out var write))
        {
            write(edictEvent, publishActivity);
        }

        var stamped = Stamp(edictEvent, entry, publishActivity);

        await stream.OnNextAsync(stamped);
        return null;
    }

    public (string StreamName, string RouteKey)? TryResolveBatchKey(
        OutboxEntry entry, EdictEvent? liveWireEvent)
    {
        var edictEvent = liveWireEvent ?? serializer.Deserialize<EdictEvent>(entry.Payload);
        return ResolveStreamAddress(edictEvent);
    }

    public async Task<IReadOnlyList<OutboxEntry>> ExecuteBatchAsync(
        IReadOnlyList<OutboxEntry> entries,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch,
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
                activities[i] = StartPublishActivity(edictEvent.GetType().Name, entry, liveWireEvents[i]);
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

        return [];
    }

    // The publish span's relationship to the staging command is decided per entry
    // from the in-memory live reference. A present reference means the publish runs
    // in the same turn that raised the event (inline drain), so the span nests under
    // the staging context as a child. A null reference means the entry was rehydrated
    // from durable state in a later drain turn (reminder / activation), possibly on
    // another silo minutes-to-hours later, so the publish is its own trace root that
    // links back to the staging command rather than nesting under a closed turn.
    static Activity? StartPublishActivity(string eventTypeName, OutboxEntry entry, EdictEvent? liveWireEvent) =>
        liveWireEvent is null
            ? EdictDiagnostics.ActivitySource.StartEdictEventPublishLinked(
                eventTypeName, ActivityExtensions.BuildLink(entry.TraceParent, entry.TraceState))
            : EdictDiagnostics.ActivitySource.StartEdictEventPublish(
                eventTypeName, ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState));

    static EdictEvent Stamp(EdictEvent edictEvent, OutboxEntry entry, Activity? publishActivity)
    {
        // EventId is the event's delivery identity, assigned once as it enters
        // the Outbox and carried on the payload — never re-minted here, so a
        // producer re-publish keeps the same id and the consumer dedup ring
        // collapses it. Trace context is stamped fresh per publish: a re-publish
        // is a genuinely new wire attempt and deserves its own span.
        //
        // Fall back to the entry's captured ids (null when the command ran with
        // no trace) — never a synthesised all-zero trace id, which a consumer's
        // ActivityTraceId.CreateFromString rejects.
        var (fallbackTraceId, fallbackSpanId) = SplitTraceParent(entry.TraceParent);

        return edictEvent with
        {
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
    (string StreamName, string RouteKey) ResolveStreamAddress(EdictEvent edictEvent) =>
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
