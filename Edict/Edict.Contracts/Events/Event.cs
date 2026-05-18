using MessagePack;

namespace Edict.Contracts.Events;

/// <summary>
/// Base for a notification that state has changed, published to a domain
/// stream after a Command Handler returns <c>Accepted</c>. Concrete events
/// derive from this and carry only their domain payload; all framework fields
/// are stamped at flush by the runtime (ADR 0011). W3C trace fields stitch
/// the <c>Command → Publish → Handle</c> chain as parent-child spans across
/// the stream hop (ADR 0003).
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public abstract record EdictEvent
{
    /// <summary>Framework-assigned identity for this event instance.</summary>
    public Guid EventId { get; init; }

    /// <summary>UTC timestamp stamped at flush, before stream publication.</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>W3C traceparent trace-id hex string; null when no active trace.</summary>
    public string? TraceId { get; init; }

    /// <summary>W3C traceparent parent-id (span-id) hex string; null when no active trace.</summary>
    public string? SpanId { get; init; }

    /// <summary>W3C tracestate; null when absent.</summary>
    public string? TraceState { get; init; }
}
