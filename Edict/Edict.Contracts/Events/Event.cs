using MessagePack;

namespace Edict.Contracts.Events;

/// <summary>
/// Base for a notification that state has changed, published to a domain
/// stream after a Command Handler returns <c>Accepted</c>. Concrete events
/// derive from this and carry only their domain payload; framework fields
/// are stamped by the runtime — <c>OccurredAt</c> inside
/// <c>EdictCommandHandler.Raise()</c>, <c>EventId</c> and trace context at
/// drain by the outbox. W3C trace fields stitch the
/// <c>Command → Publish → Handle</c> chain as parent-child spans across the
/// stream hop.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public abstract record EdictEvent
{
    /// <summary>Framework-assigned identity for this event instance.</summary>
    public Guid EventId { get; init; }

    /// <summary>
    /// UTC timestamp stamped inside <c>EdictCommandHandler.Raise()</c> at the
    /// moment the consumer's handler decided to publish the event; preserved
    /// across any subsequent outbox delay.
    /// </summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>W3C traceparent trace-id hex string; null when no active trace.</summary>
    public string? TraceId { get; init; }

    /// <summary>W3C traceparent parent-id (span-id) hex string; null when no active trace.</summary>
    public string? SpanId { get; init; }

    /// <summary>W3C tracestate; null when absent.</summary>
    public string? TraceState { get; init; }
}
