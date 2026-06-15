using Edict.Contracts.Audit;

using MessagePack;

namespace Edict.Contracts.Events;

/// <summary>
/// Base for a notification that state has changed, published to a domain
/// stream after a Command Handler returns <c>Accepted</c>. Concrete events
/// derive from this and carry only their domain payload; framework fields
/// are stamped by the runtime — <c>OccurredAt</c> inside
/// <c>EdictCommandHandler.Raise()</c>, <c>EventId</c> once as the event enters
/// the outbox, and trace context per publish at drain. W3C trace fields stitch the
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

    /// <summary>
    /// Chain-stable correlation id inherited from the message that caused this
    /// event. Framework-stamped as the event enters the outbox (from the handling
    /// Command's correlation), distinct from the per-event <see cref="EventId"/>
    /// and from W3C trace context: it is constant across the whole conversation,
    /// so it survives a Saga hop where the EventId does not and is present even
    /// when no trace is sampled.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// The actor on whose authority the causing Command was issued, inherited
    /// unchanged from that Command as the event enters the outbox, so the actor
    /// stays attributed across the whole conversation. Null when the causing
    /// Command carried no principal (auditing off, or a framework-internal event).
    /// </summary>
    public EdictPrincipal? Principal { get; init; }
}
