namespace Edict.Contracts.Audit;

// Framework-internal seam over the append-only store of captured message bodies,
// implemented per substrate (Postgres first) so Edict.Core learns no storage SDK.
// Distinct from the claim-check store, which is event-only, EventId-keyed, and on
// the delivery lifecycle: this is keyed by audit record id, holds the body of every
// captured Command and Event, and is on the audit lifecycle (append-only, infinite
// retention). Keeping the body here rather than in the hash chain keeps the chain
// personal-data-free and leaves the door open for the deferred crypto-shred — the
// body slice can be encrypted and destroyed without touching a single hash. Put
// takes a batch because a drain pass commits the bodies a grain staged in one
// grain-state write together; Put dedups on record id so a re-drain is a no-op.
internal interface IEdictAuditPayloadStore
{
    Task PutAsync(IReadOnlyList<EdictAuditPayload> payloads, CancellationToken cancellationToken);

    Task<ReadOnlyMemory<byte>> GetAsync(Guid recordId, CancellationToken cancellationToken);
}
