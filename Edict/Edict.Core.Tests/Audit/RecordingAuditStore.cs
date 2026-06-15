using System.Collections.Concurrent;

using Edict.Contracts.Audit;

namespace Edict.Core.Tests.Audit;

// In-memory IEdictAuditStore standing in for a substrate store so a Core test can
// assert the drained records without a container. Append dedups on record id the
// way the real append-only stores do (ON CONFLICT DO NOTHING), so a re-drain
// after a crash between append and ack is a no-op.
public sealed class RecordingAuditStore : IEdictAuditStore
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

    public Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, CancellationToken cancellationToken)
    {
        IReadOnlyList<EdictAuditRecord> result = _records.Values
            .Where(record => record.EntityType == entityType && record.EntityKey == entityKey)
            .OrderBy(record => record.Sequence)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        IReadOnlyList<EdictAuditRecord> result = _records.Values
            .Where(record => record.EntityType == entityType && record.EntityKey == entityKey)
            .Where(record => record.OccurredAt >= from && record.OccurredAt < to)
            .OrderBy(record => record.Sequence)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<EdictAuditRecord>> ByCorrelationAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        IReadOnlyList<EdictAuditRecord> result = _records.Values
            .Where(record => record.CorrelationId == correlationId)
            .OrderBy(record => record.OccurredAt)
            .ThenBy(record => record.EntityType, StringComparer.Ordinal)
            .ThenBy(record => record.EntityKey, StringComparer.Ordinal)
            .ThenBy(record => record.Sequence)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<EdictAuditRecord>> ByPrincipalAsync(EdictPrincipal principal, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        IReadOnlyList<EdictAuditRecord> result = _records.Values
            .Where(record => record.Principal == principal)
            .Where(record => record.OccurredAt >= from && record.OccurredAt < to)
            .OrderBy(record => record.OccurredAt)
            .ThenBy(record => record.EntityType, StringComparer.Ordinal)
            .ThenBy(record => record.EntityKey, StringComparer.Ordinal)
            .ThenBy(record => record.Sequence)
            .ToList();
        return Task.FromResult(result);
    }

    public int Count => _records.Count;
}
