namespace Edict.Azure.Tests;

/// <summary>
/// ADR 0002 idempotency proof against the real Azure Queue provider (ADR 0016).
/// Proves the dedup ring works end-to-end with Azure Queue Storage streams,
/// including the commit-on-success invariant under simulated redelivery.
/// Real queue redelivery (visibility-timeout expiry) is exercised by
/// <see cref="Real_queue_visibility_timeout_redelivery_is_handled_once"/>.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class DedupAtLeastOnceTests(AzureClusterFixture fixture)
{
    // Cycle 1 — dedup ring accepts new event via Azure Queue provider
    [Fact]
    public async Task Event_delivered_via_azure_queue_is_dispatched()
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

    // Cycle 2 — duplicate EventId is suppressed via Azure Queue provider
    [Fact]
    public async Task Duplicate_EventId_via_azure_queue_is_suppressed()
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

    // Cycle 3 — EventId committed only after dispatch succeeds (ADR-0002 commit-on-success proof).
    // Orleans' Azure Queue stream provider retries failed deliveries within the silo;
    // the dedup ring must accept the retry (EventId not yet committed after the throw)
    // and commit on the successful attempt. Subsequent deliveries must be suppressed.
    [Fact]
    public async Task EventId_committed_only_after_dispatch_succeeds_azure_provider()
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

        // Arm throw on first delivery: EventId must NOT be committed when dispatch throws.
        // Orleans retries internally, so the same event arrives again; the ring must accept
        // that retry (not suppress it as a duplicate) because the EventId was never committed.
        await grain.ArmThrowOnNextAsync();
        await publisher.PublishAsync(evt);

        // Wait for the event to be committed via retry/redelivery — handled exactly once.
        var handled = await WaitForHandledAsync(grain);
        Assert.Single(handled);
        Assert.Equal(eventId, handled[0]);

        // Further publish with same EventId: now in ring → suppressed.
        await publisher.PublishAsync(evt);
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Single(await grain.GetHandledEventIdsAsync());
    }

    // Cycle 4 — proves the Azure Queue visibility timeout causes real redelivery.
    // The AzureClusterFixture sets MessageVisibilityTimeout = 5s so that when
    // the silo receives a message but does not ack it (dispatch throws), the queue
    // makes the message visible again automatically without any test republishing.
    [Fact]
    public async Task Real_queue_visibility_timeout_redelivery_is_handled_once()
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

        // Arm throw so first delivery does not commit EventId; the Azure Queue
        // will redeliver the message after the 5-second visibility timeout.
        await grain.ArmThrowOnNextAsync();
        await publisher.PublishAsync(evt);

        // Wait long enough for the queue to redeliver (visibility timeout = 5s,
        // pull interval = 200ms; allow 20s total for CI headroom).
        var handled = await WaitForHandledAsync(grain, timeoutSeconds: 20);

        // The queue redelivered the same EventId; dedup ring accepted it once.
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
