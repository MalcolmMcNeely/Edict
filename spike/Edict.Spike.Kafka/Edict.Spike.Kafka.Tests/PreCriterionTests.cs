using Edict.Spike.Kafka.Contracts;

using Xunit;
using Xunit.Abstractions;

namespace Edict.Spike.Kafka.Tests;

public class PreCriterionTests : IClassFixture<SpikeKafkaClusterFixture>
{
    readonly SpikeKafkaClusterFixture _fx;
    readonly ITestOutputHelper _out;

    public PreCriterionTests(SpikeKafkaClusterFixture fx, ITestOutputHelper @out)
    {
        _fx = fx;
        _out = @out;
    }

    [Fact]
    public async Task MessagesDeliveredAsync_fires_after_HandleAsync_returns()
    {
        var probe = _fx.Client.GetGrain<ISpikeProbeGrain>(0);
        var recorder = _fx.Client.GetGrain<IRecorderGrain>("orders");
        var publisher = _fx.Client.GetGrain<IPublisherGrain>("pub");
        await probe.ResetAsync();
        await recorder.ResetAsync();

        var orderId = Guid.NewGuid();
        var evt = new OrderPlaced { OrderId = orderId, EventId = Guid.NewGuid(), Sequence = 1, Note = "pre-criterion probe" };
        await publisher.PublishAsync(orderId, evt);

        await WaitForObservation(recorder, expected: 1, timeout: TimeSpan.FromSeconds(30));

        var snapshot = await probe.SnapshotAsync();
        foreach (var e in snapshot)
        {
            _out.WriteLine($"{e.Ordinal,4} {e.Stamp:HH:mm:ss.fff} {e.Kind,-28} key={e.PartitionKey} part={e.Partition} off={e.Offset} eid={e.EventId}");
        }

        var enter = snapshot.SingleOrDefault(e => e.Kind == "HandleAsyncEnter" && e.EventId == evt.EventId);
        var exit = snapshot.SingleOrDefault(e => e.Kind == "HandleAsyncExit" && e.EventId == evt.EventId);
        var delivered = snapshot.FirstOrDefault(e => e.Kind == "MessagesDeliveredAsync" && (e.PartitionKey == orderId.ToString() || e.Offset != null));

        Assert.NotNull(enter);
        Assert.NotNull(exit);
        Assert.NotNull(delivered);

        _out.WriteLine($"HandleEnter={enter!.Ordinal} HandleExit={exit!.Ordinal} MessagesDelivered={delivered!.Ordinal}");

        Assert.True(delivered.Ordinal > exit.Ordinal,
            $"PRE-CRITERION FAILED: MessagesDeliveredAsync fired at ordinal {delivered.Ordinal} BEFORE HandleAsync returned (exit ordinal {exit.Ordinal}). " +
            "Orleans' pulling agent is committing offsets ahead of handler completion — at-least-once cannot be guaranteed on Kafka with this design.");
    }

    static async Task WaitForObservation(IRecorderGrain recorder, int expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var seen = await recorder.GetAllAsync();
            if (seen.Count >= expected)
            {
                return;
            }
            await Task.Delay(250);
        }
        throw new TimeoutException($"Did not observe {expected} events within {timeout}.");
    }
}
