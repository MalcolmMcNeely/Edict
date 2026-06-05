using System.Collections.Concurrent;

using Edict.Contracts.TableStorage;
using Edict.Core.TableStorage;

namespace Edict.Tests.Conformance.Streaming.References;

/// <summary>
/// In-memory <see cref="IEdictTableStoreFactory"/> the streaming battery wires as
/// its reference persistence. Table-projection grains implicitly subscribed to a
/// streaming workload's event still activate and write their rows, and the
/// dead-letter promoter still upserts forensic rows, so the streaming silo needs
/// a factory in DI — but the streaming axis never asserts on a row. This honours
/// the read/write round-trip contract and nothing else (no ETag, no
/// optimistic-concurrency, no real fault mode). Pulls no provider SDK.
/// </summary>
public sealed class ReferenceTableStoreFactory : IEdictTableStoreFactory
{
    readonly ConcurrentDictionary<string, object> _rows = new();

    static string Key(string tableName, string partitionKey, string rowKey) =>
        $"{tableName}|{partitionKey}|{rowKey}";

    public Task<IEdictTableWriteStore<T>> CreateAsync<T>(string tableName, CancellationToken cancellationToken = default)
        where T : class, new() =>
        Task.FromResult<IEdictTableWriteStore<T>>(new ReferenceTableWriteStore<T>(this, tableName));

    public Task UpsertRowAsync(string tableName, string partitionKey, string rowKey, object row, CancellationToken cancellationToken = default)
    {
        _rows[Key(tableName, partitionKey, rowKey)] = row;
        return Task.CompletedTask;
    }

    internal T? Get<T>(string tableName, string partitionKey, string rowKey) where T : class =>
        _rows.TryGetValue(Key(tableName, partitionKey, rowKey), out var row) ? (T)row : null;

    internal IReadOnlyList<T> GetPartition<T>(string tableName, string partitionKey) where T : class
    {
        var prefix = $"{tableName}|{partitionKey}|";
        return _rows
            .Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(entry => (T)entry.Value)
            .ToList();
    }

    internal void Upsert<T>(string tableName, string partitionKey, string rowKey, T row) where T : class =>
        _rows[Key(tableName, partitionKey, rowKey)] = row;
}
