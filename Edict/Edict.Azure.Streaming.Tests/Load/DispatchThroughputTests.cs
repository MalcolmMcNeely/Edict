using System.Diagnostics;

using Edict.Tests.Conformance.EventHandler;

namespace Edict.Azure.Streaming.Tests.Load;

// Deliberately loose ~3-minute wall-clock ceiling: catches order-of-magnitude
// regressions on slow CI runners without flaking. Streaming-axis throughput —
// a real Azure Queue hop with reference persistence behind it, so the measured
// cost is the dispatch path, not the durable store.
[Collection(AqsStreamingCollection.Name)]
public sealed class DispatchThroughputTests(AqsStreamingFixture fixture)
{
    const int CommandCount = 1000;
    const int MaxParallelDispatches = 32;
    static readonly TimeSpan WallClockCeiling = TimeSpan.FromMinutes(3);

    [Fact]
    public async Task Dispatch_ShouldHandleDownstreamEventExactlyOnce_When1000CommandsFan()
    {
        var customerIds = Enumerable.Range(0, CommandCount)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        var stopwatch = Stopwatch.StartNew();

        using (var gate = new SemaphoreSlim(MaxParallelDispatches))
        {
            await Task.WhenAll(customerIds.Select(async id =>
            {
                await gate.WaitAsync();
                try
                {
                    await fixture.Sender.SendAsync(new NotifyCustomerCommand(id, "load"));
                }
                finally
                {
                    gate.Release();
                }
            }));
        }

        var handledCounts = await WaitForAllHandledAsync(customerIds, stopwatch);

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < WallClockCeiling,
            $"Load test exceeded {WallClockCeiling} ceiling: observed {stopwatch.Elapsed}.");

        // Each customer's event handler sees its single event exactly once —
        // proving fan-out neither drops a dispatch nor double-handles under load.
        Assert.Equal(CommandCount, handledCounts.Count);
        Assert.All(handledCounts.Values, count => Assert.Equal(1, count));
    }

    async Task<Dictionary<Guid, int>> WaitForAllHandledAsync(
        IReadOnlyList<Guid> customerIds,
        Stopwatch stopwatch)
    {
        var hits = new Dictionary<Guid, int>(customerIds.Count);
        while (stopwatch.Elapsed < WallClockCeiling)
        {
            var pending = customerIds.Where(id => !hits.ContainsKey(id)).ToArray();
            if (pending.Length == 0)
            {
                return hits;
            }

            foreach (var batch in Chunk(pending, 50))
            {
                var fetched = await Task.WhenAll(batch.Select(async id =>
                    (Id: id, Count: await fixture.Cluster.GrainFactory
                        .GetGrain<IEmailHandlerProbe>(id).GetHandledCountAsync())));
                foreach (var (id, count) in fetched)
                {
                    if (count >= 1)
                    {
                        hits[id] = count;
                    }
                }
            }

            if (hits.Count >= customerIds.Count)
            {
                return hits;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        return hits;
    }

    static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            var slice = new List<T>(size);
            for (var j = i; j < Math.Min(i + size, source.Count); j++)
            {
                slice.Add(source[j]);
            }
            yield return slice;
        }
    }
}
