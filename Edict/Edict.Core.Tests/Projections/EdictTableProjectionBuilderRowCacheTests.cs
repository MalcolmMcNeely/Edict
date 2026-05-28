using Edict.Contracts.TableStorage;
using Edict.Core.Projections;

namespace Edict.Core.Tests.Projections;

public sealed class EdictTableProjectionBuilderRowCacheTests
{
    [Fact]
    public async Task EnsureLoadedAsync_NEventsOnSameSlot_CallsGetAsyncOnce()
    {
        var store = new CountingTableWriteStore<TestRow>();
        var slot = new TableProjectionRowSlot<TestRow>();

        for (var i = 0; i < 5; i++)
        {
            await slot.EnsureLoadedAsync(store, "pk", "rk");
        }

        Assert.Equal(1, store.GetCallCount);
    }

    [Fact]
    public async Task EnsureLoadedAsync_MEventsOnDistinctSlots_CallsGetAsyncMTimes()
    {
        var store = new CountingTableWriteStore<TestRow>();
        var slot = new TableProjectionRowSlot<TestRow>();

        await slot.EnsureLoadedAsync(store, "pk-a", "rk-1");
        await slot.EnsureLoadedAsync(store, "pk-a", "rk-2");
        await slot.EnsureLoadedAsync(store, "pk-b", "rk-1");
        await slot.EnsureLoadedAsync(store, "pk-b", "rk-2");

        Assert.Equal(4, store.GetCallCount);
    }

    sealed class TestRow
    {
        public int Counter { get; set; }
    }

    sealed class CountingTableWriteStore<T> : IEdictTableWriteStore<T> where T : class, new()
    {
        public int GetCallCount { get; private set; }

        public Task<T?> GetAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default)
        {
            GetCallCount++;
            return Task.FromResult<T?>(null);
        }

        public Task UpsertAsync(string partitionKey, string rowKey, T row, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
