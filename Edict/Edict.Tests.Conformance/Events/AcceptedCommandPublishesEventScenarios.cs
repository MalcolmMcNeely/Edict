using Edict.Contracts.Events;

using Orleans;

using Xunit;

namespace Edict.Tests.Conformance.Events;

/// <summary>
/// Substrate-agnostic conformance for the publish path: an accepted command
/// raises an event that lands on the bound substrate's domain stream with the
/// consumer-typed payload intact.
/// </summary>
public abstract class AcceptedCommandPublishesEventScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected AcceptedCommandPublishesEventScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AcceptedCommand_ShouldPublishEventToDomainStream()
    {
        var orderId = Guid.NewGuid();

        await _fixture.Sender.Send(new PlaceOrderCommand(orderId, "SKU-1"));

        var events = await EventCaptureWaiters.WaitForEventsAsync(_fixture.GrainFactory, orderId);
        var placed = Assert.IsType<OrderPlacedEvent>(Assert.Single(events));
        Assert.Equal(orderId, placed.OrderId);
        Assert.Equal("SKU-1", placed.Sku);
    }
}

static class EventCaptureWaiters
{
    public static async Task<IReadOnlyList<EdictEvent>> WaitForEventsAsync(
        IGrainFactory grainFactory, Guid aggregateId, int expectedCount = 1, int timeoutSeconds = 30)
    {
        var captureGrain = grainFactory.GetGrain<IOrderEventCaptureGrain>(aggregateId);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var events = await captureGrain.GetCapturedEventsAsync();
            if (events.Count >= expectedCount)
            {
                return events;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return await captureGrain.GetCapturedEventsAsync();
    }
}
