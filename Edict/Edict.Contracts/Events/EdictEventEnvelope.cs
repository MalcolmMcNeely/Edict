using System.ComponentModel;

using MessagePack;

namespace Edict.Contracts.Events;

/// <summary>
/// Framework-internal wire-format wrapper carrying every event that crosses
/// an Edict stream. Exactly one of
/// <see cref="InlinePayload"/> / <see cref="ClaimCheckKey"/> is set per
/// instance — small events ride the inline branch (a ~10-byte tax around
/// the MessagePack-serialised inner event), oversized events ride the
/// pointer branch (the body is written to the claim-check blob store and
/// only the key travels). Consumer <c>Handle(TEvent)</c> never sees the
/// envelope; the runtime unwraps it before dispatch.
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
    /// branch. When set, <see cref="ClaimCheckKey"/> is null.
    /// </summary>
    public byte[]? InlinePayload { get; init; }

    /// <summary>
    /// Claim-check store key pointing at the inner event's serialised
    /// body. Null on the inline branch. When set,
    /// <see cref="InlinePayload"/> is null.
    /// </summary>
    public string? ClaimCheckKey { get; init; }

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
    /// Construct an envelope on one of the two branches. Throws when both
    /// or neither is provided — the exactly-one invariant is asserted at
    /// construction.
    /// </summary>
    [SerializationConstructor]
    public EdictEventEnvelope(byte[]? inlinePayload, string? claimCheckKey)
    {
        if (inlinePayload is null == claimCheckKey is null)
        {
            throw new ArgumentException(
                "EdictEventEnvelope requires exactly one of InlinePayload or ClaimCheckKey to be set.");
        }

        InlinePayload = inlinePayload;
        ClaimCheckKey = claimCheckKey;
    }
}
