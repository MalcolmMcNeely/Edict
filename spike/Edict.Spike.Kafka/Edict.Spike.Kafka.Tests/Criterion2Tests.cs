using Edict.Spike.Kafka.Contracts;

using Xunit;
using Xunit.Abstractions;

namespace Edict.Spike.Kafka.Tests;

public class Criterion2Tests : IClassFixture<NoPubSubStoreFixture>
{
    readonly NoPubSubStoreFixture _fx;
    readonly ITestOutputHelper _out;

    public Criterion2Tests(NoPubSubStoreFixture fx, ITestOutputHelper @out)
    {
        _fx = fx;
        _out = @out;
    }

    [Fact]
    public async Task Implicit_subscription_resolves_without_PubSubStore()
    {
        var recorder = _fx.Client.GetGrain<IRecorderGrain>("orders");
        var publisher = _fx.Client.GetGrain<IPublisherGrain>("pub");
        await recorder.ResetAsync();

        var orderId = Guid.NewGuid();
        var evt = new OrderPlaced { OrderId = orderId, EventId = Guid.NewGuid(), Sequence = 1, Note = "no-pubsubstore" };
        await publisher.PublishAsync(orderId, evt);

        var seen = await WaitForObservation(recorder, 1, TimeSpan.FromSeconds(30));
        Assert.Single(seen);
        Assert.Equal(evt.EventId, seen[0].EventId);
        _out.WriteLine($"Round-trip without PubSubStore observed event {seen[0].EventId}");
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
        throw new TimeoutException($"Did not observe {expected} events within {timeout}.");
    }
}
