using Edict.Contracts.Events;

namespace Edict.Azure.Tests.Events;

/// <summary>
/// Azurite/Testcontainers conformance for the publish path: an accepted
/// command raises an event that lands on the real Azure Queue domain stream
/// with the consumer-typed payload intact. Lifted from
/// <c>EventPublishingTests</c> in Core.Tests.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class AcceptedCommandPublishesEventTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task AcceptedCommand_ShouldPublishEventToDomainStream()
    {
        var orderId = Guid.NewGuid();

        await fixture.Sender.Send(new AzurePlaceOrderCommand(orderId, "SKU-1"));

        var events = await EventCaptureWaiters.WaitForEventsAsync(fixture, orderId);
        var placed = Assert.IsType<AzureOrderPlacedEvent>(Assert.Single(events));
        Assert.Equal(orderId, placed.OrderId);
        Assert.Equal("SKU-1", placed.Sku);
    }
}

static class EventCaptureWaiters
{
    public static async Task<IReadOnlyList<EdictEvent>> WaitForEventsAsync(
        AzureClusterFixture fixture, Guid aggregateId, int expectedCount = 1, int timeoutSeconds = 30)
    {
        var captureGrain = fixture.Cluster.GrainFactory.GetGrain<IAzureOrderEventCaptureGrain>(aggregateId);
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
