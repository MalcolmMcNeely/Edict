using System.Diagnostics;

using Edict.Azure.TableStorage;
using Edict.Contracts.Configuration;

namespace Edict.Azure.Tests.Load;

/// <summary>
/// Liveness + invariants load test (parent PRD #83, this slice #98).
/// Dispatches 1000 commands across 1000 unique aggregates against Azurite, then
/// waits for the downstream projection effect (not the handler return) and
/// asserts three invariants:
/// <list type="bullet">
/// <item>No message loss — exactly 1000 rows appear in the projection table.</item>
/// <item>Exactly-once effect — every row has <c>OrderCount == 1</c>, so any
/// at-least-once redelivery was suppressed by the dedup ring rather than
/// double-applied.</item>
/// <item>Dedup ring stays bounded — a sample of projection grains' persisted
/// idempotency state shows the ring sized to
/// <see cref="EdictOptions.IdempotencyWindowSize"/>, catching a regression
/// that swaps the bounded ring for an unbounded structure.</item>
/// </list>
/// Deliberately loose ~2-minute wall-clock ceiling so the test catches
/// order-of-magnitude regressions on slow CI runners without flaking. Runs in
/// the default CI build, not behind a flag.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class DispatchThroughputTests(AzureClusterFixture fixture)
{
    const int CommandCount = 1000;
    const int MaxParallelDispatches = 32;
    const int RingProbeSampleSize = 10;
    static readonly TimeSpan WallClockCeiling = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task Dispatch_ShouldDeliverDownstreamEffectExactlyOnce_When1000CommandsFan()
    {
        var orderIds = Enumerable.Range(0, CommandCount)
            .Select(_ => Guid.NewGuid())
            .ToArray();
        var repository = new AzureTableRepository<AzureOrderTableRow>(
            fixture.TableServiceClient, "azureorderprojection");

        var stopwatch = Stopwatch.StartNew();

        using (var gate = new SemaphoreSlim(MaxParallelDispatches))
        {
            await Task.WhenAll(orderIds.Select(async id =>
            {
                await gate.WaitAsync();
                try
                {
                    await fixture.Sender.Send(new AzurePlaceOrderCommand(id, "SKU-LOAD"));
                }
                finally
                {
                    gate.Release();
                }
            }));
        }

        var rows = await WaitForAllRowsAsync(repository, orderIds, stopwatch);

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < WallClockCeiling,
            $"Load test exceeded {WallClockCeiling} ceiling: observed {stopwatch.Elapsed}.");

        // Invariant 1 — no message loss.
        Assert.Equal(CommandCount, rows.Count);

        // Invariant 2 — exactly-once effect across all 1000 commands.
        Assert.All(rows.Values, row => Assert.Equal(1, row.OrderCount));

        // Invariant 3 — dedup ring is bounded at the configured WindowSize on
        // a sample of projection grains. Per-aggregate consumers each see one
        // event, so Count == 1; what matters is that Capacity matches the
        // configured ring size and has not silently grown.
        var defaultWindowSize = new EdictOptions().IdempotencyWindowSize;
        foreach (var orderId in orderIds.Take(RingProbeSampleSize))
        {
            var probe = fixture.Cluster.GrainFactory
                .GetGrain<IAzureOrderTableProjectionProbe>(orderId);
            var ring = await probe.GetRingStateAsync();
            Assert.Equal(defaultWindowSize, ring.Capacity);
            Assert.Equal(1, ring.Count);
        }
    }

    static async Task<Dictionary<Guid, AzureOrderTableRow>> WaitForAllRowsAsync(
        AzureTableRepository<AzureOrderTableRow> repository,
        IReadOnlyList<Guid> orderIds,
        Stopwatch stopwatch)
    {
        var hits = new Dictionary<Guid, AzureOrderTableRow>(orderIds.Count);
        while (stopwatch.Elapsed < WallClockCeiling)
        {
            var pending = orderIds.Where(id => !hits.ContainsKey(id)).ToArray();
            if (pending.Length == 0)
            {
                return hits;
            }

            foreach (var batch in Chunk(pending, 50))
            {
                var fetched = await Task.WhenAll(batch.Select(async id =>
                    (Id: id, Row: await repository.GetAsync(id.ToString(), id.ToString()))));
                foreach (var (id, row) in fetched)
                {
                    if (row is not null && row.OrderCount >= 1)
                    {
                        hits[id] = row;
                    }
                }
            }

            if (hits.Count >= orderIds.Count)
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
