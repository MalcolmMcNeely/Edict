namespace Edict.Azure.Tests.Idempotency;

/// <summary>
/// An event type the consumer does not handle must not consume a dedup ring
/// slot: <see cref="Core.Idempotency.EdictIdempotencyBase{TPayload}.DispatchAsync"/>
/// returning <c>false</c> short-circuits before <c>Commit</c>. Proven against
/// the real Azure Queue stream provider — the publisher routes both the
/// handled and the unhandled events onto the same <c>AzureDedupTest</c> stream,
/// so the consumer sees both deliveries on its real implicit subscription and
/// the ring's contents are observable by replaying the first id.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class UnhandledEventTypeRingSlotTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task HandleAsync_ShouldConsumeNoRingSlot_WhenEventTypeIsUnhandled()
    {
        var grainId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureDedupPublisherGrain>(grainId);
        var consumer = fixture.Cluster.GrainFactory.GetGrain<IAzureDedupTestConsumer>(grainId);

        // Fill the ring (size 3) with handled events.
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

        // Publish an unhandled event type onto the same AzureDedupTest stream;
        // DispatchAsync returns false for it so no ring slot should be consumed.
        var unhandled = new AzureOrderPlacedEvent(grainId, "SKU-UNHANDLED") with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };
        await publisher.PublishAsync(unhandled);
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Re-publish id1 — if the unhandled event had evicted it from the ring,
        // it would dispatch again. Correct behaviour: still suppressed.
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
