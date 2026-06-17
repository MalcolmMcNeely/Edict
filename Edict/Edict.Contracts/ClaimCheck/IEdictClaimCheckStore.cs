using Edict.Contracts.Tenancy;

namespace Edict.Contracts.ClaimCheck;

// Framework-internal seam over the append-only blob store that backs the
// claim-check escape hatch for oversized events. The surface is deliberately
// minimal — only PutAsync and GetAsync. No DeleteAsync because the store is
// append-only by design; retention is owned by the storage account's lifecycle
// policy so a framework bug or a configuration mistake cannot erase forensic
// evidence. No ExistsAsync because it would invite TOCTOU races between the
// existence check and the subsequent fetch; missing blobs surface as a GetAsync
// exception which the receiver pipeline funnels into the dead-letter promotion
// path. The store does not mint a key: the parked body is addressed by the
// event's own EventId folded behind the tenant wall, so every store encodes the
// same EdictKeyComposer.Compose(tenant, EventId) string as its backend key
// (Azure blob name, Postgres id, in-memory dictionary key) rather than inventing
// a key shape that could disagree with another store. Folding the tenant keeps a
// tenant-scoped event's spilled body — the largest, most sensitive payload Edict
// ever parks — out of reach of another tenant's wall; a public event keeps the
// bare EventId key.
internal interface IEdictClaimCheckStore
{
    Task PutAsync(EdictTenantId? tenant, Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

    Task<ReadOnlyMemory<byte>> GetAsync(EdictTenantId? tenant, Guid eventId, CancellationToken cancellationToken);
}
