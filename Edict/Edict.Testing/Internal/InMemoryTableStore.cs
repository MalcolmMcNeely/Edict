using System.Collections.Concurrent;

using Edict.Contracts.TableStorage;
using Edict.Core.TableStorage;

namespace Edict.Testing.Internal;

/// <summary>Non-generic upsert seam so the factory can write a row whose
/// concrete type is only known at drain time (the Outbox UpsertRow path).</summary>
interface IInMemoryUpsert
{
    void UpsertObject(string partitionKey, string rowKey, object row);
    IEnumerable<(string PartitionKey, string RowKey, object Row)> All();
}

/// <summary>
/// In-memory write store backing one (tableName, T) pair. Idempotent by
/// (partitionKey, rowKey) — a full-row replace — so the Outbox's at-least-once
/// UpsertRow redelivery does not double-apply.
/// </summary>
sealed class InMemoryTableStore<T> : IEdictTableWriteStore<T>, IEdictTableRepository<T>, IInMemoryUpsert
    where T : class, new()
{
    readonly ConcurrentDictionary<(string pk, string rk), T> _rows = new();

    public void UpsertObject(string partitionKey, string rowKey, object row) =>
        _rows[(partitionKey, rowKey)] = (T)row;

    public IEnumerable<(string, string, object)> All() =>
        _rows.Select(kv => (kv.Key.pk, kv.Key.rk, (object)kv.Value!));

    public Task<T?> GetAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_rows.TryGetValue((partitionKey, rowKey), out var row) ? row : null);

    public Task UpsertAsync(string partitionKey, string rowKey, T row, CancellationToken cancellationToken = default)
    {
        _rows[(partitionKey, rowKey)] = row;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<T>> QueryPartitionAsync(string partitionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<T>>(
            _rows.Where(kv => kv.Key.pk == partitionKey).Select(kv => kv.Value).ToList());
}

/// <summary>
/// The shipped in-memory <see cref="IEdictTableStoreFactory"/>: one store per
/// (tableName, T). Caches stores so a test can read back the rows a projection
/// builder wrote without any Azure dependency.
/// </summary>
sealed class InMemoryTableStoreFactory : IEdictTableStoreFactory
{
    readonly ConcurrentDictionary<string, object> _stores = new();

    public Task<IEdictTableWriteStore<T>> CreateAsync<T>(string tableName, CancellationToken cancellationToken = default)
        where T : class, new() =>
        Task.FromResult((IEdictTableWriteStore<T>)_stores.GetOrAdd(
            $"{tableName}:{typeof(T).FullName}", _ => new InMemoryTableStore<T>()));

    public Task UpsertRowAsync(string tableName, string partitionKey, string rowKey, object row, CancellationToken cancellationToken = default)
    {
        var store = _stores.GetOrAdd(
            $"{tableName}:{row.GetType().FullName}",
            _ => Activator.CreateInstance(typeof(InMemoryTableStore<>).MakeGenericType(row.GetType()))!);
        ((IInMemoryUpsert)store).UpsertObject(partitionKey, rowKey, row);
        return Task.CompletedTask;
    }

    /// <summary>Every row currently held, across all tables — the source for
    /// the timeline's projection-state section.</summary>
    public IEnumerable<(string Table, string PartitionKey, string RowKey, object Row)> AllRows() =>
        _stores.SelectMany(kv =>
            ((IInMemoryUpsert)kv.Value).All()
                .Select(r => (kv.Key.Split(':')[0], r.PartitionKey, r.RowKey, r.Row)));
}
