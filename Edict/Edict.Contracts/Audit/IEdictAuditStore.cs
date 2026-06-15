namespace Edict.Contracts.Audit;

// Framework-internal seam over the append-only, queryable audit chain store,
// implemented per substrate (Postgres first) so Edict.Core learns no storage
// SDK. Deliberately minimal — append and read-by-entity. No update or delete:
// the store is WORM by design, so a framework bug or configuration mistake
// cannot rewrite the evidence; retention is an operator policy, infinite by
// default. Append takes a batch because a drain pass commits the records a grain
// staged in one grain-state write together.
internal interface IEdictAuditStore
{
    Task AppendAsync(IReadOnlyList<EdictAuditRecord> records, CancellationToken cancellationToken);

    Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, CancellationToken cancellationToken);
}
