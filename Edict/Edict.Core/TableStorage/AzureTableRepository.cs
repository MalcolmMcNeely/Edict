using Azure;
using Azure.Data.Tables;

using Edict.Contracts.TableStorage;

namespace Edict.Core.TableStorage;

/// <summary>
/// Azure Table Storage implementation of <see cref="ITableRepository{T}"/>.
/// Registered by the consumer's DI setup; <see cref="ITableRepository{T}"/> is
/// the substitution seam in <c>Edict.Contracts</c> (ADR 0008 / ADR 0012).
/// </summary>
public sealed class AzureTableRepository<T> : ITableRepository<T>
    where T : class, ITableEntity, new()
{
    private readonly TableClient _tableClient;

    public AzureTableRepository(TableServiceClient tableServiceClient, string tableName)
    {
        _tableClient = tableServiceClient.GetTableClient(tableName);
    }

    public async Task<T?> GetAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<T>(
                partitionKey, rowKey, cancellationToken: cancellationToken);
            return response.Value;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<T>> QueryPartitionAsync(
        string partitionKey,
        CancellationToken cancellationToken = default)
    {
        var results = new List<T>();
        try
        {
            await foreach (var entity in _tableClient.QueryAsync<T>(
                filter: $"PartitionKey eq '{partitionKey}'",
                cancellationToken: cancellationToken))
            {
                results.Add(entity);
            }
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // Table does not exist yet — return empty list.
        }
        return results;
    }
}
