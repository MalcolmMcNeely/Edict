using Edict.Spike.Kafka.Adapter;
using Edict.Spike.Kafka.Contracts;

using Xunit;
using Xunit.Abstractions;

namespace Edict.Spike.Kafka.Tests;

public class Criterion5Tests : IClassFixture<CrashFixture>
{
    readonly CrashFixture _fx;
    readonly ITestOutputHelper _out;

    public Criterion5Tests(CrashFixture fx, ITestOutputHelper @out)
    {
        _fx = fx;
        _out = @out;
    }

    [Fact]
    public async Task MidBatch_crash_redelivers_unhandled_events()
    {
        var consumerGroup = $"spike-c5-{Guid.NewGuid():N}";
        SpikeFaultInjection.Reset();

        var orderId = Guid.NewGuid();
        var evt1Id = Guid.NewGuid();
        var evt2Id = Guid.NewGuid();
        var evt1 = new OrderPlaced { OrderId = orderId, EventId = evt1Id, Sequence = 1, Note = "c5 first" };
        var evt2 = new OrderPlaced { OrderId = orderId, EventId = evt2Id, Sequence = 2, Note = "c5 second (hung)" };

        SpikeFaultInjection.ArmHang(evt2Id);

        var topic = $"spike-c5-{Guid.NewGuid():N}";
        var clusterA = await _fx.DeployClusterAsync(consumerGroup, topic);
        try
        {
            var publisher = clusterA.Client.GetGrain<IPublisherGrain>("pub");
            await publisher.PublishManyAsync(orderId, new[] { evt1, evt2 });

            using var enteredCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await SpikeFaultInjection.WaitEnteredAsync(evt2Id).WaitAsync(enteredCts.Token);
            _out.WriteLine("Cluster A: event 2 handler entered (hung). Force-killing silo.");

            foreach (var silo in clusterA.Silos)
            {
                await clusterA.KillSiloAsync(silo);
            }
        }
        finally
        {
            try { clusterA.Dispose(); } catch { }
        }

        SpikeFaultInjection.Reset();
        _out.WriteLine("Re-deploying cluster B on the same consumer group.");

        var clusterB = await _fx.DeployClusterAsync(consumerGroup, topic);
        try
        {
            var recorder = clusterB.Client.GetGrain<IRecorderGrain>("orders");
            var seen = await WaitForObservation(recorder, evt2Id, TimeSpan.FromSeconds(60));

            var event2Records = seen.Where(e => e.EventId == evt2Id).ToList();
            Assert.True(event2Records.Count >= 1,
                $"Cluster B did NOT redeliver event 2 ({evt2Id}); saw events {string.Join(", ", seen.Select(e => e.EventId))}.");
            _out.WriteLine($"Cluster B observed event 2 redelivery. Total events seen by B: {seen.Count}, event-2 redeliveries: {event2Records.Count}.");
        }
        finally
        {
            try { await clusterB.StopAllSilosAsync(); } catch { }
            clusterB.Dispose();
        }
    }

    static async Task<List<OrderPlaced>> WaitForObservation(IRecorderGrain recorder, Guid expectedId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var seen = await recorder.GetAllAsync();
            if (seen.Any(e => e.EventId == expectedId))
            {
                return seen;
            }
            await Task.Delay(500);
        }
        var partial = await recorder.GetAllAsync();
        throw new TimeoutException($"Did not observe event {expectedId} within {timeout}; saw {partial.Count} events.");
    }
}
