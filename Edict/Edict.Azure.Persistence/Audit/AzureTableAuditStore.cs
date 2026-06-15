using System.Linq.Expressions;

using Azure;
using Azure.Data.Tables;

using Edict.Contracts.Audit;

using Orleans.Serialization;

namespace Edict.Azure.Persistence.Audit;

// Azure-table audit chain store. Azure Table has no secondary index, so the store
// fans each record out to one append row per access path — an entity-partition row,
// a correlation-partition row, and (when attributed) a principal-partition row — so
// every query path reaches its records by a single partition scan. The fan-out is
// cheap because the store is append-only and Postgres, not Azure, is the throughput
// substrate. Each row carries the full serialized record (chain hashes included), so
// a query deserializes the record back whole; the row's own columns exist only to
// scope and time-filter the scan. The chain is per-aggregate and never globally
// sequenced, so ByCorrelation and ByPrincipal rebuild cross-grain order at query
// time from intent-time, with entity and sequence as a stable tie-break. Append rows
// are insert-once (a re-drain's duplicate insert is swallowed): the chain hash is the
// tamper-evidence layer, but there is no infra prevention here until the deferred
// blob-sealing slice, where Postgres has both via its WORM trigger.
sealed class AzureTableAuditStore : IEdictAuditStore
{
    readonly TableClient _tableClient;
    readonly Serializer _serializer;

    AzureTableAuditStore(TableClient tableClient, Serializer serializer)
    {
        _tableClient = tableClient;
        _serializer = serializer;
    }

    // Provisions the backing table. Run once on the host thread at silo wiring — a
    // lazy factory that calls GetAwaiter().GetResult() on first resolve deadlocks on
    // the grain task scheduler when the audit drain reaches for the store.
    public static Task ProvisionAsync(
        TableServiceClient tableServiceClient,
        string tableName,
        CancellationToken cancellationToken = default) =>
        tableServiceClient.GetTableClient(tableName).CreateIfNotExistsAsync(cancellationToken);

    // Wraps an already-provisioned table; no I/O, safe to call from the DI factory
    // that resolves the Serializer.
    public static AzureTableAuditStore Create(TableServiceClient tableServiceClient, Serializer serializer, string tableName) =>
        new(tableServiceClient.GetTableClient(tableName), serializer);

    // Provision-and-wrap, for fresh dev/test environments that have no AppHost to
    // pre-provision the table against the storage account.
    public static async Task<AzureTableAuditStore> CreateAsync(
        TableServiceClient tableServiceClient,
        Serializer serializer,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        await ProvisionAsync(tableServiceClient, tableName, cancellationToken);
        return Create(tableServiceClient, serializer, tableName);
    }

    // Internal so the Azure tamper-evidence conformance test can address the
    // entity-path row directly, the layout-aware analogue of the Postgres WORM test
    // naming the chain table and column.
    internal static string EntityPartition(string entityType, string entityKey) =>
        $"E_{AzureAuditKeyEncoding.Encode(entityType)}_{AzureAuditKeyEncoding.Encode(entityKey)}";

    // The column each fan-out row stores the serialized record under.
    internal const string RecordColumn = nameof(AuditRowEntity.Record);

    static string CorrelationPartition(Guid correlationId) => $"C_{correlationId:N}";

    static string PrincipalPartition(EdictPrincipal principal) => $"P_{AzureAuditKeyEncoding.Encode(principal.Value)}";

    public async Task AppendAsync(IReadOnlyList<EdictAuditRecord> records, CancellationToken cancellationToken)
    {
        foreach (var record in records)
        {
            var serialized = _serializer.SerializeToArray(record);
            var rowKey = record.RecordId.ToString("N");

            await TryAddRowAsync(EntityPartition(record.EntityType, record.EntityKey), rowKey, record.OccurredAt, serialized, cancellationToken);
            await TryAddRowAsync(CorrelationPartition(record.CorrelationId), rowKey, record.OccurredAt, serialized, cancellationToken);
            if (record.Principal is { } principal)
            {
                await TryAddRowAsync(PrincipalPartition(principal), rowKey, record.OccurredAt, serialized, cancellationToken);
            }
        }
    }

    async Task TryAddRowAsync(string partitionKey, string rowKey, DateTimeOffset occurredAt, byte[] serialized, CancellationToken cancellationToken)
    {
        var entity = new AuditRowEntity
        {
            PartitionKey = partitionKey,
            RowKey = rowKey,
            OccurredAt = occurredAt,
            Record = serialized,
        };
        try
        {
            await _tableClient.AddEntityAsync(entity, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 409)
        {
            // The row for this record id is already on this access path; a re-drain
            // must leave the original untouched.
        }
    }

    public Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, CancellationToken cancellationToken)
    {
        var partition = EntityPartition(entityType, entityKey);
        return QueryAsync(
            entity => entity.PartitionKey == partition,
            BySequence,
            cancellationToken);
    }

    public Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var partition = EntityPartition(entityType, entityKey);
        return QueryAsync(
            entity => entity.PartitionKey == partition && entity.OccurredAt >= from && entity.OccurredAt < to,
            BySequence,
            cancellationToken);
    }

    public Task<IReadOnlyList<EdictAuditRecord>> ByCorrelationAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        var partition = CorrelationPartition(correlationId);
        return QueryAsync(
            entity => entity.PartitionKey == partition,
            ByIntentTime,
            cancellationToken);
    }

    public Task<IReadOnlyList<EdictAuditRecord>> ByPrincipalAsync(EdictPrincipal principal, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var partition = PrincipalPartition(principal);
        return QueryAsync(
            entity => entity.PartitionKey == partition && entity.OccurredAt >= from && entity.OccurredAt < to,
            ByIntentTime,
            cancellationToken);
    }

    async Task<IReadOnlyList<EdictAuditRecord>> QueryAsync(
        Expression<Func<AuditRowEntity, bool>> filter,
        Func<IEnumerable<EdictAuditRecord>, IOrderedEnumerable<EdictAuditRecord>> order,
        CancellationToken cancellationToken)
    {
        var records = new List<EdictAuditRecord>();
        await foreach (var entity in _tableClient.QueryAsync(filter, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            records.Add(_serializer.Deserialize<EdictAuditRecord>(entity.Record));
        }
        return order(records).ToList();
    }

    // The chain is per-aggregate, so an entity scan follows the stored sequence.
    static IOrderedEnumerable<EdictAuditRecord> BySequence(IEnumerable<EdictAuditRecord> records) =>
        records.OrderBy(record => record.Sequence);

    // A correlation or principal can fan across grains with no single sequence to
    // follow; intent-time rebuilds the cross-grain order, with entity and sequence as
    // a stable tie-break when two records share a timestamp.
    static IOrderedEnumerable<EdictAuditRecord> ByIntentTime(IEnumerable<EdictAuditRecord> records) =>
        records
            .OrderBy(record => record.OccurredAt)
            .ThenBy(record => record.EntityType, StringComparer.Ordinal)
            .ThenBy(record => record.EntityKey, StringComparer.Ordinal)
            .ThenBy(record => record.Sequence);

    sealed class AuditRowEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = "";
        public string RowKey { get; set; } = "";
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
        public byte[] Record { get; set; } = [];
    }
}
