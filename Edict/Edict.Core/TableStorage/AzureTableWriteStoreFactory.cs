using Azure.Data.Tables;

using Edict.Contracts.TableStorage;

namespace Edict.Core.TableStorage;

/// <summary>
/// Azure Table Storage implementation of <see cref="IEdictTableStoreFactory"/> (ADR 0015).
/// Creates a <see cref="AzureTableWriteStore{T}"/> for each named table and ensures the
/// table exists before the grain starts consuming events.
/// </summary>
public sealed class AzureTableWriteStoreFactory : IEdictTableStoreFactory
{
    private readonly TableServiceClient _tableServiceClient;

    public AzureTableWriteStoreFactory(TableServiceClient tableServiceClient)
    {
        _tableServiceClient = tableServiceClient;
    }

    public async Task<IEdictTableWriteStore<T>> CreateAsync<T>(
        string tableName,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var tableClient = _tableServiceClient.GetTableClient(tableName);
        await tableClient.CreateIfNotExistsAsync(cancellationToken);
        return new AzureTableWriteStore<T>(tableClient);
    }
}
