using Edict.Contracts.TableStorage;
using Edict.Core.TableStorage;

namespace Edict.Core.Tests.TableStorage;

/// <summary>
/// In-memory write-store for testing. Shared between silo and test code via a
/// <see cref="InMemoryTableStoreFactory"/> static singleton so tests can inspect
/// rows written by grains without Azure.
/// </summary>
public sealed class InMemoryTableWriteStore<T> : IEdictTableWriteStore<T>
    where T : class, new()
{
    private readonly Dictionary<(string pk, string rk), T> _rows = new();

    public T? Get(string partitionKey, string rowKey) =>
        _rows.TryGetValue((partitionKey, rowKey), out var row) ? row : null;

    public IReadOnlyList<T> GetPartition(string partitionKey) =>
        _rows.Where(kv => kv.Key.pk == partitionKey).Select(kv => kv.Value).ToList();

    public Task<T?> GetAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Get(partitionKey, rowKey));

    public Task UpsertAsync(
        string partitionKey,
        string rowKey,
        T row,
        CancellationToken cancellationToken = default)
    {
        _rows[(partitionKey, rowKey)] = row;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Test factory that creates (and caches) one <see cref="InMemoryTableWriteStore{T}"/>
/// per (tableName, T) pair. Register as a singleton in the silo and expose via the
/// fixture so tests can read back rows the grain wrote.
/// </summary>
public sealed class InMemoryTableStoreFactory : IEdictTableStoreFactory
{
    private readonly Dictionary<string, object> _stores = new();

    public Task<IEdictTableWriteStore<T>> CreateAsync<T>(
        string tableName,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var key = $"{tableName}:{typeof(T).FullName}";
        if (!_stores.TryGetValue(key, out var existing))
        {
            existing = new InMemoryTableWriteStore<T>();
            _stores[key] = existing;
        }
        return Task.FromResult((IEdictTableWriteStore<T>)existing);
    }

    public InMemoryTableWriteStore<T> GetStore<T>(string tableName)
        where T : class, new() =>
        (InMemoryTableWriteStore<T>)_stores[$"{tableName}:{typeof(T).FullName}"];
}
