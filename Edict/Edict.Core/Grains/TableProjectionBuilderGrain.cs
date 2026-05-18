using Azure;
using Azure.Data.Tables;

using Edict.Contracts.Events;

namespace Edict.Core.Grains;

/// <summary>
/// Projection builder whose read model lives in Azure Table Storage so grain
/// activation stays small regardless of how large the model grows (ADR 0012).
/// The <see cref="EdictEventDeduplicationGrain"/> dedup ring stays in persisted grain
/// state as usual — the row write and ring commit are two non-atomic stores, and
/// the resulting crash-window double-apply is accepted until the Outbox ships.
/// </summary>
public abstract class EdictTableProjectionBuilderGrain<T> : EdictProjectionBuilderGrain
    where T : class, ITableEntity, new()
{
    private readonly TableServiceClient _tableServiceClient;
    private TableClient? _tableClient;

    protected EdictTableProjectionBuilderGrain(TableServiceClient tableServiceClient)
    {
        _tableServiceClient = tableServiceClient;
    }

    /// <summary>The Azure Table Storage table name for this projection.</summary>
    protected abstract string TableName { get; }

    /// <summary>
    /// Derives the RowKey from the incoming event. The PartitionKey defaults to
    /// <see cref="DefaultPartitionKey"/> (the grain's primary key, which equals the
    /// event's <c>[EdictRouteKey]</c> value for per-aggregate projections).
    /// </summary>
    protected abstract string GetRowKey(EdictEvent evt);

    /// <summary>
    /// The grain's primary key as a string. For per-aggregate projections this equals
    /// the event's <c>[EdictRouteKey]</c> Guid, making it the natural PartitionKey.
    /// Global-singleton projections override to use a different strategy.
    /// </summary>
    protected string DefaultPartitionKey => this.GetPrimaryKey().ToString();

    /// <summary>
    /// The entity loaded (or freshly constructed) before each handler invocation.
    /// Modifications the handler makes to this instance are written back to the table
    /// after the handler returns.
    /// </summary>
    protected T CurrentRow { get; private set; } = new();

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await GetTableClient().CreateIfNotExistsAsync(cancellationToken);
    }

    /// <summary>
    /// Wraps every handler call with load-apply-writeback. The base
    /// <see cref="EdictProjectionBuilderGrain.DispatchEventAsync{TEvent}"/> default is a
    /// direct handler call; this override adds the table I/O around it.
    /// </summary>
    protected override async Task DispatchEventAsync<TEvent>(TEvent evt, Func<TEvent, Task> handler)
    {
        var partitionKey = DefaultPartitionKey;
        var rowKey = GetRowKey(evt);

        var existing = await LoadFromTableAsync(partitionKey, rowKey);
        CurrentRow = existing ?? new T { PartitionKey = partitionKey, RowKey = rowKey };

        await handler(evt);

        await WriteToTableAsync(CurrentRow);
    }

    private async Task<T?> LoadFromTableAsync(string partitionKey, string rowKey)
    {
        try
        {
            var response = await GetTableClient().GetEntityAsync<T>(partitionKey, rowKey);
            return response.Value;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    private Task WriteToTableAsync(T entity) =>
        GetTableClient().UpsertEntityAsync(entity, TableUpdateMode.Replace);

    private TableClient GetTableClient() =>
        _tableClient ??= _tableServiceClient.GetTableClient(TableName);
}
