using Edict.Contracts.TableStorage;

namespace Edict.Tests.Conformance.Streaming.References;

/// <summary>
/// The per-table write store <see cref="ReferenceTableStoreFactory"/> hands back.
/// Forwards to the shared in-memory backing store so projection writes and reads
/// round-trip, honouring the contract and nothing more.
/// </summary>
sealed class ReferenceTableWriteStore<T>(ReferenceTableStoreFactory factory, string tableName) : IEdictTableWriteStore<T>
    where T : class, new()
{
    public Task<T?> GetAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(factory.Get<T>(tableName, partitionKey, rowKey));

    public Task<IReadOnlyList<T>> QueryPartitionAsync(string partitionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(factory.GetPartition<T>(tableName, partitionKey));

    public Task UpsertAsync(string partitionKey, string rowKey, T row, CancellationToken cancellationToken = default)
    {
        factory.Upsert(tableName, partitionKey, rowKey, row);
        return Task.CompletedTask;
    }
}
