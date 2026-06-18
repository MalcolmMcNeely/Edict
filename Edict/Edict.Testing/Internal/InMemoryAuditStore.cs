using System.Collections.Concurrent;

using Edict.Contracts.Audit;
using Edict.Contracts.Tenancy;

namespace Edict.Testing.Internal;

// In-memory IEdictAuditStore shipped with the Test Framework so a consumer asserts
// captured audit behaviour with no container. Append-only and dedups on record id
// the way the substrate stores do (ON CONFLICT DO NOTHING), so a re-drain after a
// crash between append and ack is a no-op; the read paths rebuild global order from
// intent-time the way a real query would. Overwrite backs the TamperWithAuditRecord
// seam — a consumer rewriting a stored row to prove the chain verifier catches the
// alteration — and is the one mutation a WORM store never offers in production.
sealed class InMemoryAuditStore : IEdictAuditStore
{
    readonly ConcurrentDictionary<Guid, EdictAuditRecord> _records = new();

    public Task AppendAsync(IReadOnlyList<EdictAuditRecord> records, CancellationToken cancellationToken)
    {
        foreach (var record in records)
        {
            _records.TryAdd(record.RecordId, record);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, EdictTenantId? tenant, CancellationToken cancellationToken)
    {
        IReadOnlyList<EdictAuditRecord> result = _records.Values
            .Where(record => record.EntityType == entityType && record.EntityKey == entityKey)
            .Where(record => InWall(record, tenant))
            .OrderBy(record => record.Sequence)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, DateTimeOffset from, DateTimeOffset to, EdictTenantId? tenant, CancellationToken cancellationToken)
    {
        IReadOnlyList<EdictAuditRecord> result = _records.Values
            .Where(record => record.EntityType == entityType && record.EntityKey == entityKey)
            .Where(record => record.OccurredAt >= from && record.OccurredAt < to)
            .Where(record => InWall(record, tenant))
            .OrderBy(record => record.Sequence)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<EdictAuditRecord>> ByConversationAsync(Guid correlationId, EdictTenantId? tenant, CancellationToken cancellationToken)
    {
        IReadOnlyList<EdictAuditRecord> result = _records.Values
            .Where(record => record.ConversationId == correlationId)
            .Where(record => InWall(record, tenant))
            .OrderBy(record => record.OccurredAt)
            .ThenBy(record => record.EntityType, StringComparer.Ordinal)
            .ThenBy(record => record.EntityKey, StringComparer.Ordinal)
            .ThenBy(record => record.Sequence)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<EdictAuditRecord>> ByPrincipalAsync(EdictPrincipal principal, DateTimeOffset from, DateTimeOffset to, EdictTenantId? tenant, CancellationToken cancellationToken)
    {
        IReadOnlyList<EdictAuditRecord> result = _records.Values
            .Where(record => record.Principal == principal)
            .Where(record => record.OccurredAt >= from && record.OccurredAt < to)
            .Where(record => InWall(record, tenant))
            .OrderBy(record => record.OccurredAt)
            .ThenBy(record => record.EntityType, StringComparer.Ordinal)
            .ThenBy(record => record.EntityKey, StringComparer.Ordinal)
            .ThenBy(record => record.Sequence)
            .ToList();
        return Task.FromResult(result);
    }

    // A null filter is the operator superset across every wall; a non-null filter
    // is the in-store predicate that keeps another tenant's rows from ever leaving.
    static bool InWall(EdictAuditRecord record, EdictTenantId? tenant) =>
        tenant is null || record.Tenant == tenant;

    // Replaces a stored record in place, keyed by its record id. The one mutation
    // a production WORM store refuses, offered here only so the tamper seam can
    // rewrite a row and let the chain verifier catch the broken hash.
    public void Overwrite(EdictAuditRecord rewritten) => _records[rewritten.RecordId] = rewritten;
}
