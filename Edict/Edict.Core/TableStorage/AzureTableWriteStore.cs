using Azure;
using Azure.Data.Tables;

using Edict.Contracts.TableStorage;

namespace Edict.Core.TableStorage;

/// <summary>
/// Azure Table Storage implementation of <see cref="IEdictTableWriteStore{T}"/> (ADR 0015).
/// Maps plain POCO rows to/from <see cref="TableEntity"/> so the grain base stays
/// provider-neutral. Constructed per table by <see cref="AzureTableWriteStoreFactory"/>.
/// </summary>
internal sealed class AzureTableWriteStore<T> : IEdictTableWriteStore<T>
    where T : class, new()
{
    private readonly TableClient _tableClient;

    internal AzureTableWriteStore(TableClient tableClient)
    {
        _tableClient = tableClient;
    }

    public async Task<T?> GetAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>(
                partitionKey, rowKey, cancellationToken: cancellationToken);
            return AzureTablePocoMapper.FromTableEntity<T>(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public Task UpsertAsync(
        string partitionKey,
        string rowKey,
        T row,
        CancellationToken cancellationToken = default)
    {
        var entity = AzureTablePocoMapper.ToTableEntity(partitionKey, rowKey, row);
        return _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }
}
