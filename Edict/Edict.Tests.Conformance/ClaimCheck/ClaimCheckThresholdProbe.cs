using Edict.Contracts.Events;

using Orleans.Serialization;

namespace Edict.Tests.Conformance.ClaimCheck;

/// <summary>
/// Computes the claim-check inline/pointer threshold for the boundary scenario as
/// the serialized size of a probe event whose payload is
/// <see cref="ReferencePayloadLength"/> characters long. Wiring it inside the
/// silo's policy factory — against the silo's own serializer and the fixture's
/// frozen-clock epoch — means a live event raised with that same payload length
/// serialises to exactly the threshold (so it stays inline), and one byte longer
/// crosses it (so it spills to a pointer). No byte-count magic survives outside
/// this one helper.
/// </summary>
public static class ClaimCheckThresholdProbe
{
    /// <summary>
    /// Both this length and one past it sit in the MessagePack <c>str8</c> band
    /// (32..255 bytes), so each added character is exactly one more serialized
    /// byte — the over-threshold payload is exactly one byte larger than the
    /// at-threshold one, with no length-prefix step in between.
    /// </summary>
    public const int ReferencePayloadLength = 64;

    /// <summary>
    /// The serialized size of a <see cref="ThresholdProbeEvent"/> carrying a
    /// payload of <paramref name="payloadLength"/> characters, stamped at
    /// <paramref name="occurredAt"/>. The variable-width fields are pinned: the
    /// timestamp by the supplied instant, the ids by being fixed-width Guids
    /// regardless of value, so the size depends only on the payload length.
    /// </summary>
    public static int SerializedSize(Serializer serializer, int payloadLength, DateTimeOffset occurredAt)
    {
        var probe = new ThresholdProbeEvent(Guid.NewGuid(), new string('x', payloadLength))
        {
            EventId = Guid.NewGuid(),
            OccurredAt = occurredAt,
        };
        return serializer.SerializeToArray<EdictEvent>(probe).Length;
    }
}
