using System.ComponentModel;

namespace Edict.Core.Audit;

/// <summary>
/// One staged audit record on the <see cref="AuditChain"/>: its id (the drain's
/// ack key), the serialized <c>EdictAuditRecord</c>, and the captured message
/// <see cref="Payload"/>. All held as bytes, like <c>OutboxEntry</c>, so the
/// generated envelope serializer never reaches into a contract type; the drain
/// writes the body to the payload store and deserializes the record through the
/// contract serializer before writing it to the chain store.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[GenerateSerializer]
[Alias("AuditChainEntry")]
public sealed record AuditChainEntry
{
    /// <summary>Record id, the key the drain acks by.</summary>
    [Id(0)]
    public Guid RecordId { get; init; }

    /// <summary>The serialized <c>EdictAuditRecord</c>, opaque to the slice.</summary>
    [Id(1)]
    public byte[] Record { get; init; } = [];

    /// <summary>The serialized message body, the exact bytes the record's payload hash was computed over; drained to the payload store keyed by <see cref="RecordId"/>.</summary>
    [Id(2)]
    public byte[] Payload { get; init; } = [];
}
