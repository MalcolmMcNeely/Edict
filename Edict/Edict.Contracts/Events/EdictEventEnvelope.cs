using MessagePack;

namespace Edict.Contracts.Events;

/// <summary>
/// Framework-internal wire-format wrapper carrying every event that crosses
/// an Edict stream (ADR 0024). Exactly one of
/// <see cref="InlinePayload"/> / <see cref="ClaimCheckKey"/> is set per
/// instance — small events ride the inline branch (a ~10-byte tax around
/// the MessagePack-serialised inner event), oversized events ride the
/// pointer branch (the body is written to the claim-check blob store and
/// only the key travels). Consumer <c>Handle(TEvent)</c> never sees the
/// envelope; the runtime unwraps it before dispatch.
/// <para>
/// Wire-format identity is the class name plus the property names (ADR 0007
/// MessagePack keyAsPropertyName). The class name is frozen — a rename
/// would break in-flight events on the queue and persisted entries in
/// every Outbox grain document (ADR 0017 spirit, satisfied via property
/// names in this Orleans-free assembly).
/// </para>
/// </summary>
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
    /// Construct an envelope on one of the two branches. Throws when both
    /// or neither is provided — the exactly-one invariant is asserted at
    /// construction (ADR 0024).
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
