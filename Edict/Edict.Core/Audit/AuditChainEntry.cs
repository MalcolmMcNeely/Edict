using System.ComponentModel;

namespace Edict.Core.Audit;

/// <summary>
/// One staged audit record on the <see cref="AuditChain"/>: its id (the drain's
/// ack key) and the serialized <c>EdictAuditRecord</c>. Held as bytes, like
/// <c>OutboxEntry</c>, so the generated envelope serializer never reaches into a
/// contract type; the drain deserializes through the contract serializer before
/// writing to the store.
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
}
