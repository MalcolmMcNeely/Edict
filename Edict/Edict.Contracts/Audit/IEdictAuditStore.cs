namespace Edict.Contracts.Audit;

// Framework-internal seam over the append-only, queryable audit chain store,
// implemented per substrate (Postgres first) so Edict.Core learns no storage
// SDK. No update or delete: the store is WORM by design, so a framework bug or
// configuration mistake cannot rewrite the evidence; retention is an operator
// policy, infinite by default. Append takes a batch because a drain pass commits
// the records a grain staged in one grain-state write together. The read paths
// rebuild global order at query time from the records' intent-time, since the
// chain is per-aggregate and never globally sequenced.
internal interface IEdictAuditStore
{
    Task AppendAsync(IReadOnlyList<EdictAuditRecord> records, CancellationToken cancellationToken);

    Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    Task<IReadOnlyList<EdictAuditRecord>> ByCorrelationAsync(Guid correlationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EdictAuditRecord>> ByPrincipalAsync(EdictPrincipal principal, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
