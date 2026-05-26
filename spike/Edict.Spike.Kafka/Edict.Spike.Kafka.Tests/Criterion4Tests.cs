using Edict.Spike.Kafka.Adapter;
using Edict.Spike.Kafka.Contracts;

using Xunit;
using Xunit.Abstractions;

namespace Edict.Spike.Kafka.Tests;

public class Criterion4Tests : IClassFixture<CrashFixture>
{
    readonly CrashFixture _fx;
    readonly ITestOutputHelper _out;

    public Criterion4Tests(CrashFixture fx, ITestOutputHelper @out)
    {
        _fx = fx;
        _out = @out;
    }

    [Fact]
    public async Task MidHandler_crash_redelivers_after_restart()
    {
        var consumerGroup = $"spike-c4-{Guid.NewGuid():N}";
        SpikeFaultInjection.Reset();

        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var evt = new OrderPlaced { OrderId = orderId, EventId = eventId, Sequence = 1, Note = "c4 mid-handler hang" };
        SpikeFaultInjection.ArmHang(eventId);

        var topic = $"spike-c4-{Guid.NewGuid():N}";
        var clusterA = await _fx.DeployClusterAsync(consumerGroup, topic);
        try
        {
            var publisher = clusterA.Client.GetGrain<IPublisherGrain>("pub");
            await publisher.PublishAsync(orderId, evt);

            using var enteredCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await SpikeFaultInjection.WaitEnteredAsync(eventId).WaitAsync(enteredCts.Token);
            _out.WriteLine("Cluster A handler entered (hung). Force-killing silo.");

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
            var seen = await WaitForObservation(recorder, 1, TimeSpan.FromSeconds(60));
            Assert.Contains(seen, e => e.EventId == eventId);
            _out.WriteLine($"Cluster B observed redelivery of event {eventId} (total events seen: {seen.Count})");
        }
        finally
        {
            try { await clusterB.StopAllSilosAsync(); } catch { }
            clusterB.Dispose();
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
            await Task.Delay(500);
        }
        var partial = await recorder.GetAllAsync();
        throw new TimeoutException($"Did not observe {expected} events within {timeout}; saw {partial.Count}.");
    }
}
