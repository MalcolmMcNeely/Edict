namespace Edict.Azure.Tests;

[Collection(AzureClusterCollection.Name)]
public sealed class DedupAtLeastOnceTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task HandleAsync_ShouldDispatch_WhenEventDeliveredViaAzureQueue()
    {
        var grainId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureDedupPublisherGrain>(grainId);
        var grain = fixture.Cluster.GrainFactory.GetGrain<IAzureDedupTestConsumer>(grainId);

        var evt = new AzureDedupTestEvent(grainId, 1) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };
        await publisher.PublishAsync(evt);

        var handled = await WaitForHandledAsync(grain);
        Assert.Single(handled);
        Assert.Equal(evt.EventId, handled[0]);
    }

    [Fact]
    public async Task HandleAsync_ShouldSuppressDuplicate_WhenEventIdDuplicatedViaAzureQueue()
    {
        var grainId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureDedupPublisherGrain>(grainId);
        var grain = fixture.Cluster.GrainFactory.GetGrain<IAzureDedupTestConsumer>(grainId);

        var sharedEventId = Guid.NewGuid();
        var first = new AzureDedupTestEvent(grainId, 1) with
        {
            EventId = sharedEventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };
        var duplicate = new AzureDedupTestEvent(grainId, 2) with
        {
            EventId = sharedEventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(first);
        await WaitForHandledAsync(grain, expectedCount: 1);

        await publisher.PublishAsync(duplicate);
        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Single(await grain.GetHandledEventIdsAsync());
    }

    [Fact]
    public async Task HandleAsync_ShouldCommitEventIdOnlyAfterDispatchSucceeds()
    {
        var grainId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureDedupPublisherGrain>(grainId);
        var grain = fixture.Cluster.GrainFactory.GetGrain<IAzureDedupTestConsumer>(grainId);

        var eventId = Guid.NewGuid();
        var evt = new AzureDedupTestEvent(grainId, 1) with
        {
            EventId = eventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await grain.ArmThrowOnNextAsync();
        await publisher.PublishAsync(evt);

        var handled = await WaitForHandledAsync(grain);
        Assert.Single(handled);
        Assert.Equal(eventId, handled[0]);

        await publisher.PublishAsync(evt);
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Single(await grain.GetHandledEventIdsAsync());
    }

    [Fact]
    public async Task HandleAsync_ShouldHandleRedeliveryOnce_WhenQueueVisibilityTimeoutExpires()
    {
        var grainId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureDedupPublisherGrain>(grainId);
        var grain = fixture.Cluster.GrainFactory.GetGrain<IAzureDedupTestConsumer>(grainId);

        var eventId = Guid.NewGuid();
        var evt = new AzureDedupTestEvent(grainId, 1) with
        {
            EventId = eventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };

        // Throws on first delivery so the EventId is not committed; the Azure
        // Queue will redeliver the message after the 5s visibility timeout.
        await grain.ArmThrowOnNextAsync();
        await publisher.PublishAsync(evt);

        var handled = await WaitForHandledAsync(grain, timeoutSeconds: 20);

        Assert.Single(handled);
        Assert.Equal(eventId, handled[0]);
    }

    private static async Task<IReadOnlyList<Guid>> WaitForHandledAsync(
        IAzureDedupTestConsumer grain,
        int expectedCount = 1,
        int timeoutSeconds = 15)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var ids = await grain.GetHandledEventIdsAsync();
            if (ids.Count >= expectedCount)
                return ids;
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return await grain.GetHandledEventIdsAsync();
    }
}
