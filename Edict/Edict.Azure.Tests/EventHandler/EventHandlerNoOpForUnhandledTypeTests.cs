using Edict.Contracts.Events;

namespace Edict.Azure.Tests.EventHandler;

[Collection(AzureClusterCollection.Name)]
public sealed class EventHandlerNoOpForUnhandledTypeTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task EventHandler_ShouldNotInvokeHandle_WhenAzureQueueDeliversUnhandledEventType()
    {
        var aggregateId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureEmailEventPublisher>(aggregateId);
        var handler = fixture.Cluster.GrainFactory.GetGrain<IAzureEmailHandlerProbe>(aggregateId);

        var unhandled = new AzureUnhandledEvent(aggregateId, 1) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(unhandled);

        // A re-stage via InvokeHandler would land within the fixture's 5s
        // queue visibility window; 3s here is enough to confirm no dispatch.
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(0, await handler.GetHandledCountAsync());
    }
}
