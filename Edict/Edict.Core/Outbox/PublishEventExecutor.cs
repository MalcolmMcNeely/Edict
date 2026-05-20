using Edict.Contracts.Events;
using Edict.Telemetry;

using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Outbox;

/// <summary>
/// Drains a <see cref="OutboxEffectKind.PublishEvent"/> entry: deserialize the
/// buffered event, stamp identity/time/trace, and publish to its domain stream
/// (ADR 0018). The entry's captured <c>traceparent</c> is restored as the
/// publish span's parent — never a span link — so the
/// <c>Command → Publish → Handle</c> tree is identical to the pre-Outbox code
/// and survives a crash-recovery drain (ADR 0003). Bare-named.
/// </summary>
sealed class PublishEventExecutor(Serializer serializer) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    public async Task ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task>? deferredDispatch)
    {
        var evt = serializer.Deserialize<EdictEvent>(entry.Payload);
        var (streamName, routeKey) = ResolveStreamAddress(evt);
        var stream = streamProvider.GetStream<EdictEvent>(StreamId.Create(streamName, routeKey));

        var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);

        using var publishActivity = EdictDiagnostics.ActivitySource.StartEdictEventPublish(
            evt.GetType().Name, parentContext);

        // Fall back to the entry's captured ids (null when the command ran with
        // no trace) — never a synthesised all-zero trace id, which a consumer's
        // ActivityTraceId.CreateFromString rejects (ADR 0003).
        var (fallbackTraceId, fallbackSpanId) = SplitTraceParent(entry.TraceParent);

        var stamped = evt with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            TraceId = publishActivity?.TraceId.ToHexString() ?? fallbackTraceId,
            SpanId = publishActivity?.SpanId.ToHexString() ?? fallbackSpanId,
            TraceState = publishActivity?.TraceStateString ?? entry.TraceState,
        };

        await stream.OnNextAsync(stamped);
    }

    // A claim-checked event rides as an EdictEventEnvelope whose inner-event
    // address fields name the domain stream the unwrapped event would have
    // ridden (ADR 0024, slice 2). The envelope itself carries no [EdictStream]
    // because the stream choice is data, not metadata. Slice-3 receiver-side
    // unwrap will pick the envelope off this stream and rehydrate the inner
    // event.
    static (string StreamName, Guid RouteKey) ResolveStreamAddress(EdictEvent evt) =>
        evt is EdictEventEnvelope envelope && envelope.InnerEventStreamName is { } streamName
            ? (streamName, envelope.InnerEventRouteKey)
            : EventStreamAddress.Resolve(evt);

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
