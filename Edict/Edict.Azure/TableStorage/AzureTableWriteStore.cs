using Azure;
using Azure.Data.Tables;

using Edict.Contracts.TableStorage;

namespace Edict.Azure.TableStorage;

internal sealed class AzureTableWriteStore<T> : IEdictTableWriteStore<T>
    where T : class, new()
{
    readonly TableClient _tableClient;

    internal AzureTableWriteStore(TableClient tableClient)
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
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public Task UpsertAsync(string partitionKey, string rowKey, T row, CancellationToken cancellationToken = default)
    {
        var entity = AzureTablePocoMapper.ToTableEntity(partitionKey, rowKey, row);
        return _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }
}
