using Azure.Data.Tables;

using Edict.Contracts.TableStorage;
using Edict.Core.TableStorage;

namespace Edict.Azure.Persistence.TableStorage;

internal sealed class AzureTableWriteStoreFactory(TableServiceClient tableServiceClient) : IEdictTableStoreFactory
{
    public async Task<IEdictTableWriteStore<T>> CreateAsync<T>(string tableName, CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var tableClient = tableServiceClient.GetTableClient(tableName);
        await tableClient.CreateIfNotExistsAsync(cancellationToken);
        return new AzureTableWriteStore<T>(tableClient);
    }

    public async Task UpsertRowAsync(
        string tableName,
        string partitionKey,
        string rowKey,
        object row,
        CancellationToken cancellationToken = default)
    {
        var tableClient = tableServiceClient.GetTableClient(tableName);
        await tableClient.CreateIfNotExistsAsync(cancellationToken);
        var entity = AzureTablePocoMapper.ToTableEntity(partitionKey, rowKey, row);
        await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }
}
