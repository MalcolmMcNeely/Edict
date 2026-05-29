namespace Edict.Contracts.ClaimCheck;

// Framework-internal seam over the append-only blob store that backs the
// claim-check escape hatch for oversized events. The surface is deliberately
// minimal — only PutAsync and GetAsync. No DeleteAsync because the store is
// append-only by design; retention is owned by the storage account's lifecycle
// policy so a framework bug or a configuration mistake cannot erase forensic
// evidence. No ExistsAsync because it would invite TOCTOU races between the
// existence check and the subsequent fetch; missing blobs surface as a GetAsync
// exception which the receiver pipeline funnels into the dead-letter promotion
// path. Key generation is the store's responsibility — neither the publisher
// nor the receiver attempts to derive or interpret the key string.
internal interface IEdictClaimCheckStore
{
    Task<string> PutAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

    Task<ReadOnlyMemory<byte>> GetAsync(string key, CancellationToken cancellationToken);
}
