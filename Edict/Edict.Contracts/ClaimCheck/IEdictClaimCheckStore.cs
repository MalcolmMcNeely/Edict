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
// event's own EventId, so every store encodes that one well-known Guid as its
// backend key (Azure blob name, Postgres uuid id, in-memory dictionary key)
// rather than inventing a key shape that could disagree with another store.
internal interface IEdictClaimCheckStore
{
    Task PutAsync(Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

    Task<ReadOnlyMemory<byte>> GetAsync(Guid eventId, CancellationToken cancellationToken);
}
