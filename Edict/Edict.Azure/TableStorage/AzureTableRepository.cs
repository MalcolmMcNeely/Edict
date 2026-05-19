using Azure;
using Azure.Data.Tables;

using Edict.Contracts.TableStorage;

namespace Edict.Azure.TableStorage;

/// <summary>
/// Azure Table Storage implementation of <see cref="IEdictTableRepository{T}"/>.
/// Registered by the consumer's DI setup; <see cref="IEdictTableRepository{T}"/> is
/// the substitution seam in <c>Edict.Contracts</c> (ADR 0008 / ADR 0012 / ADR 0015).
/// Maps plain POCO rows via <see cref="AzureTablePocoMapper"/>.
/// </summary>
public sealed class AzureTableRepository<T>(TableServiceClient tableServiceClient, string tableName) : IEdictTableRepository<T>
    where T : class, new()
{
    readonly TableClient _tableClient = tableServiceClient.GetTableClient(tableName);

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
                cancellationToken: cancellationToken))
            {
                results.Add(AzureTablePocoMapper.FromTableEntity<T>(entity));
            }
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // Table does not exist yet — return empty list.
        }
        return results;
    }
}
