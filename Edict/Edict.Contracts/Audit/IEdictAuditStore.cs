using Edict.Contracts.Tenancy;

namespace Edict.Contracts.Audit;

// Framework-internal seam over the append-only, queryable audit chain store,
// implemented per substrate (Postgres first) so Edict.Core learns no storage
// SDK. No update or delete: the store is WORM by design, so a framework bug or
// configuration mistake cannot rewrite the evidence; retention is an operator
// policy, infinite by default. Append takes a batch because a drain pass commits
// the records a grain staged in one grain-state write together. The read paths
// rebuild global order at query time from the records' intent-time, since the
// chain is per-aggregate and never globally sequenced. A non-null tenant filter
// narrows a read to one wall in the store itself, so an ambient-scoped read never
// pulls another tenant's rows out of storage; a null filter sees every wall.
internal interface IEdictAuditStore
{
    Task AppendAsync(IReadOnlyList<EdictAuditRecord> records, CancellationToken cancellationToken);

    Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, EdictTenantId? tenant, CancellationToken cancellationToken);

    Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, DateTimeOffset from, DateTimeOffset to, EdictTenantId? tenant, CancellationToken cancellationToken);

    Task<IReadOnlyList<EdictAuditRecord>> ByCorrelationAsync(Guid correlationId, EdictTenantId? tenant, CancellationToken cancellationToken);

    Task<IReadOnlyList<EdictAuditRecord>> ByPrincipalAsync(EdictPrincipal principal, DateTimeOffset from, DateTimeOffset to, EdictTenantId? tenant, CancellationToken cancellationToken);
}
