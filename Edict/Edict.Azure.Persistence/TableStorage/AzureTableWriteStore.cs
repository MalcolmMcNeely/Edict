using Azure;
using Azure.Data.Tables;

using Edict.Contracts.TableStorage;

namespace Edict.Azure.Persistence.TableStorage;

/// <summary>
/// Azure Table backing store for a projection's rows. Public because the
/// throughput harness constructs one directly to read rows store-direct, the
/// shape the deleted public repository served; the framework otherwise resolves
/// it through <c>IEdictTableStoreFactory</c>.
/// </summary>
public sealed class AzureTableWriteStore<T> : IEdictTableWriteStore<T>
    where T : class, new()
{
    readonly TableClient _tableClient;

    /// <summary>Wraps a ready table client. The harness passes <c>tableServiceClient.GetTableClient(name)</c>.</summary>
    public AzureTableWriteStore(TableClient tableClient)
    {
        _tableClient = tableClient;
    }

    public async Task<T?> GetAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>(partitionKey, rowKey, cancellationToken: cancellationToken);
            return AzureTablePocoMapper.FromTableEntity<T>(response.Value);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<T>> QueryPartitionAsync(string partitionKey, CancellationToken cancellationToken = default)
    {
        var results = new List<T>();
        try
        {
            await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
                filter: $"PartitionKey eq '{partitionKey}'",
                cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                results.Add(AzureTablePocoMapper.FromTableEntity<T>(entity));
            }
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
        }
        return results;
    }

    public Task UpsertAsync(string partitionKey, string rowKey, T row, CancellationToken cancellationToken = default)
    {
        var entity = AzureTablePocoMapper.ToTableEntity(partitionKey, rowKey, row);
        return _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }
}
