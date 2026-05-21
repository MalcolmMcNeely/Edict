namespace Edict.Azure.Tests.Idempotency;

[Collection(AzureClusterCollection.Name)]
public sealed class UnhandledEventTypeRingSlotTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task HandleAsync_ShouldConsumeNoRingSlot_WhenEventTypeIsUnhandled()
    {
        var grainId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureDedupPublisherGrain>(grainId);
        var consumer = fixture.Cluster.GrainFactory.GetGrain<IAzureDedupTestConsumer>(grainId);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        await publisher.PublishAsync(new AzureDedupTestEvent(grainId, 1) with
        {
            EventId = id1,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await publisher.PublishAsync(new AzureDedupTestEvent(grainId, 2) with
        {
            EventId = id2,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await publisher.PublishAsync(new AzureDedupTestEvent(grainId, 3) with
        {
            EventId = id3,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await WaitForHandledCountAsync(consumer, expectedCount: 3);

        var unhandled = new AzureOrderPlacedEvent(grainId, "SKU-UNHANDLED") with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };
        await publisher.PublishAsync(unhandled);
        await Task.Delay(TimeSpan.FromSeconds(3));

        // If the unhandled event had evicted id1 from the ring it would
        // dispatch again — correct behaviour is still suppressed.
        await publisher.PublishAsync(new AzureDedupTestEvent(grainId, 1) with
        {
            EventId = id1,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await Task.Delay(TimeSpan.FromSeconds(3));

        var handled = await consumer.GetHandledEventIdsAsync();
        Assert.Equal(3, handled.Count);
    }

    static async Task<IReadOnlyList<Guid>> WaitForHandledCountAsync(
        IAzureDedupTestConsumer consumer,
        int expectedCount,
        int timeoutSeconds = 20)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var ids = await consumer.GetHandledEventIdsAsync();
            if (ids.Count >= expectedCount)
            {
                return ids;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return await consumer.GetHandledEventIdsAsync();
    }
}
