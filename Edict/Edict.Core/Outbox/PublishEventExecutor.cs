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

    public async Task ExecuteAsync(OutboxEntry entry, IStreamProvider streamProvider)
    {
        var evt = serializer.Deserialize<EdictEvent>(entry.Payload);
        var (streamName, routeKey) = EventStreamAddress.Resolve(evt);
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
