using Edict.Contracts.Events;
using Edict.Telemetry;

using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Outbox;

sealed class PublishEventExecutor(Serializer serializer, IEventStreamAccessors accessors) : IOutboxEffectExecutor
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
        var evt = liveWireEvent ?? serializer.Deserialize<EdictEvent>(entry.Payload);
        var (streamName, routeKey) = ResolveStreamAddress(evt);
        var stream = streamProvider.GetStream<EdictEvent>(StreamId.Create(streamName, routeKey));

        var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);

        using var publishActivity = EdictDiagnostics.ActivitySource.StartEdictEventPublish(
            evt.GetType().Name, parentContext);

        // Fall back to the entry's captured ids (null when the command ran with
        // no trace) — never a synthesised all-zero trace id, which a consumer's
        // ActivityTraceId.CreateFromString rejects.
        var (fallbackTraceId, fallbackSpanId) = SplitTraceParent(entry.TraceParent);

        var stamped = evt with
        {
            EventId = Guid.NewGuid(),
            TraceId = publishActivity?.TraceId.ToHexString() ?? fallbackTraceId,
            SpanId = publishActivity?.SpanId.ToHexString() ?? fallbackSpanId,
            TraceState = publishActivity?.TraceStateString ?? entry.TraceState,
        };

        await stream.OnNextAsync(stamped);
    }

    // A claim-checked event rides as an EdictEventEnvelope whose inner-event
    // address fields name the domain stream the unwrapped event would have
    // ridden. The envelope itself carries no [EdictStream] because the
    // stream choice is data, not metadata. The receiver-side unwrap picks
    // the envelope off this stream and rehydrates the inner event.
    (string StreamName, Guid RouteKey) ResolveStreamAddress(EdictEvent evt) =>
        evt is EdictEventEnvelope envelope && envelope.InnerEventStreamName is { } streamName
            ? (streamName, envelope.InnerEventRouteKey)
            : accessors.Resolve(evt);

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
