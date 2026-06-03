using System.ComponentModel;

using MessagePack;

namespace Edict.Contracts.Events;

/// <summary>
/// Framework-internal wire-format wrapper carrying every event that crosses
/// an Edict stream. The claim-check branch is discriminated by
/// <see cref="InlinePayload"/>: small events ride the inline branch (a ~10-byte
/// tax around the MessagePack-serialised inner event), oversized events ride the
/// pointer branch (<see cref="InlinePayload"/> is null — the body is written to
/// the claim-check store keyed by the envelope's <see cref="EdictEvent.EventId"/>
/// and the receiver fetches it by that same id). Consumer <c>HandleAsync(TEvent)</c>
/// never sees the envelope; the runtime unwraps it before dispatch.
/// <para>
/// Stays <c>public</c> only because MessagePack's
/// <c>DynamicObjectResolver</c> emits IL formatters via Reflection.Emit and
/// refuses non-public types. The <see cref="EditorBrowsableAttribute"/> hides
/// it from consumer IntelliSense; the class name is frozen because a rename
/// would break in-flight events on the queue and persisted entries in every
/// Outbox grain document.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[MessagePackObject(keyAsPropertyName: true)]
public sealed record EdictEventEnvelope : EdictEvent
{
    /// <summary>
    /// Serialised body of the inner event. Null on the pointer-bearing
    /// (claim-check) branch, where the body is parked in the claim-check store
    /// under the envelope's <see cref="EdictEvent.EventId"/>.
    /// </summary>
    public byte[]? InlinePayload { get; init; }

    /// <summary>
    /// The inner event's <c>[EdictStream]</c> name, so the framework can
    /// address the envelope onto the same domain stream the unwrapped event
    /// would have ridden. <c>null</c> on the inline branch when the inner
    /// event itself is published directly (the publisher-side conditional
    /// wrap).
    /// </summary>
    public string? InnerEventStreamName { get; init; }

    /// <summary>
    /// The inner event's <c>[EdictRouteKey]</c> value, used together with
    /// <see cref="InnerEventStreamName"/> to address the envelope onto the
    /// inner event's domain stream.
    /// </summary>
    public Guid InnerEventRouteKey { get; init; }

    /// <summary>
    /// Construct an envelope on one of the two branches. The pointer branch
    /// (<paramref name="inlinePayload"/> null) requires a non-empty
    /// <paramref name="eventId"/> — the id is what locates the parked body in
    /// the claim-check store, so an empty id is neither a valid inline payload
    /// nor a valid pointer.
    /// </summary>
    [SerializationConstructor]
    public EdictEventEnvelope(byte[]? inlinePayload, Guid eventId)
    {
        if (inlinePayload is null && eventId == Guid.Empty)
        {
            throw new ArgumentException(
                "EdictEventEnvelope on the claim-check pointer branch (InlinePayload null) requires a non-empty EventId to locate the parked body.");
        }

        InlinePayload = inlinePayload;
        EventId = eventId;
    }
}
