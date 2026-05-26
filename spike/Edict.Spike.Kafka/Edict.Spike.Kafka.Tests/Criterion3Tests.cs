using Edict.Spike.Kafka.Contracts;

using Xunit;
using Xunit.Abstractions;

namespace Edict.Spike.Kafka.Tests;

public class Criterion3Tests : IClassFixture<SpikeKafkaClusterFixture>
{
    readonly SpikeKafkaClusterFixture _fx;
    readonly ITestOutputHelper _out;

    public Criterion3Tests(SpikeKafkaClusterFixture fx, ITestOutputHelper @out)
    {
        _fx = fx;
        _out = @out;
    }

    [Fact]
    public async Task PerAggregate_order_is_preserved_under_parallel_publishes()
    {
        const int aggregateCount = 8;
        const int eventsPerAggregate = 10;

        var recorder = _fx.Client.GetGrain<IRecorderGrain>("orders");
        await recorder.ResetAsync();

        var aggregateIds = Enumerable.Range(0, aggregateCount).Select(_ => Guid.NewGuid()).ToArray();

        var publishTasks = aggregateIds.Select(async (aggregateId, idx) =>
        {
            var publisher = _fx.Client.GetGrain<IPublisherGrain>($"pub-{idx}");
            for (var seq = 0; seq < eventsPerAggregate; seq++)
            {
                var evt = new OrderPlaced
                {
                    OrderId = aggregateId,
                    EventId = Guid.NewGuid(),
                    Sequence = seq,
                    Note = $"agg{idx}-seq{seq}",
                };
                await publisher.PublishAsync(aggregateId, evt);
            }
        }).ToArray();

        await Task.WhenAll(publishTasks);

        var expectedTotal = aggregateCount * eventsPerAggregate;
        var seen = await WaitForObservation(recorder, expectedTotal, TimeSpan.FromSeconds(60));
        Assert.Equal(expectedTotal, seen.Count);

        var byAggregate = seen.GroupBy(e => e.OrderId).ToDictionary(g => g.Key, g => g.ToList());
        Assert.Equal(aggregateCount, byAggregate.Count);

        foreach (var (aggregateId, events) in byAggregate)
        {
            var sequences = events.Select(e => e.Sequence).ToArray();
            var sortedAscending = sequences.Zip(sequences.Skip(1), (a, b) => a < b).All(x => x);
            Assert.True(sortedAscending,
                $"Aggregate {aggregateId} saw out-of-order sequence: [{string.Join(", ", sequences)}]");
            _out.WriteLine($"Aggregate {aggregateId} preserved order: [{string.Join(", ", sequences)}]");
        }
    }

    static async Task<List<OrderPlaced>> WaitForObservation(IRecorderGrain recorder, int expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var seen = await recorder.GetAllAsync();
            if (seen.Count >= expected)
            {
                return seen;
            }
            await Task.Delay(250);
        }
        var partial = await recorder.GetAllAsync();
        throw new TimeoutException($"Did not observe {expected} events within {timeout}; saw {partial.Count}.");
    }
}
