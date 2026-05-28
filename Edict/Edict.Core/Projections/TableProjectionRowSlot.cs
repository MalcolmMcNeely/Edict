using Edict.Contracts.TableStorage;

namespace Edict.Core.Projections;

// Last-touched-slot row cache for EdictTableProjectionBuilder<T>: per-aggregate
// projections hit one row per grain (the binding constraint on the Events row),
// so a single cached (pk, rk, row) tuple collapses the substrate GetAsync to
// once per slot. The single-writer-per-row invariant — Orleans
// (grainType, primaryKey) placement plus PartitionKey defaulting to grain key —
// is what makes the cache safe without an invalidation hook: activation drains
// pending UpsertRow effects before the first event is served, so the slot
// never observes a stale row. Many-rows-per-grain patterns miss on key change
// and fall through to today's GetAsync(pk, rk) ?? new T().
sealed class TableProjectionRowSlot<T> where T : class, new()
{
    string? _cachedPartitionKey;
    string? _cachedRowKey;

    public T CurrentRow { get; set; } = new();

    public async Task EnsureLoadedAsync(IEdictTableWriteStore<T> writeStore, string partitionKey, string rowKey)
    {
        if (partitionKey == _cachedPartitionKey && rowKey == _cachedRowKey)
        {
            return;
        }

        CurrentRow = await writeStore.GetAsync(partitionKey, rowKey) ?? new T();
        _cachedPartitionKey = partitionKey;
        _cachedRowKey = rowKey;
    }
}
