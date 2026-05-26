using Edict.Contracts.TableStorage;

namespace Edict.Tests.Conformance.Projections;

static class TableProjectionWaiters
{
    public static async Task WaitForRowAsync(
        IEdictTableRepository<OrderTableRow> repository,
        string partitionKey,
        string rowKey,
        int minOrderCount = 1,
        int timeoutSeconds = 30)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await repository.GetAsync(partitionKey, rowKey);
            if (row is not null && row.OrderCount >= minOrderCount)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    public static async Task WaitForRowAsync(
        IEdictTableRepository<OrderTableRow> repository,
        string partitionKey,
        string rowKey,
        Func<OrderTableRow, bool> predicate,
        int timeoutSeconds = 45)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await repository.GetAsync(partitionKey, rowKey);
            if (row is not null && predicate(row))
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    public static async Task WaitForPartitionCountAsync(
        IEdictTableRepository<OrderTableRow> repository,
        string partitionKey,
        int expectedCount,
        int timeoutSeconds = 30)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var rows = await repository.QueryPartitionAsync(partitionKey);
            if (rows.Count >= expectedCount)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
