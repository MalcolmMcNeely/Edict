namespace Edict.Azure.Tests.Idempotency;

/// <summary>
/// The dedup ring lives in the persisted <c>GrainEnvelope.Idempotency</c> slot
/// and must survive grain deactivation: a redelivery of an
/// already-handled event id after the grain reactivates must still be
/// suppressed. The Azurite-backed cluster writes <c>edict-state</c> to real
/// Azure Blob grain storage, so this proof exercises the same
/// substrate the sample silo wires in production — a stronger guarantee than
/// the previous in-memory grain-storage version.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class RingSurvivesDeactivationTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task DedupRing_ShouldSurviveGrainDeactivation()
    {
        var grainId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureDedupPublisherGrain>(grainId);
        var consumer = fixture.Cluster.GrainFactory.GetGrain<IAzureDedupTestConsumer>(grainId);

        // First delivery: handled, ring contains idX, state persisted to Azure Blob.
        var idX = Guid.NewGuid();
        var evtX = new AzureDedupTestEvent(grainId, 1) with
        {
            EventId = idX,
            OccurredAt = DateTimeOffset.UtcNow,
        };
        await publisher.PublishAsync(evtX);
        await WaitForHandledCountAsync(consumer, expectedCount: 1);

        // Deactivate the consumer; the persisted ring must survive in edict-state.
        await consumer.DeactivateSelfAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));

        // After reactivation: replaying idX must be suppressed; a new id idY
        // must dispatch (proves the grain reactivated against the same key and
        // is processing the stream again).
        var idY = Guid.NewGuid();
        var evtY = new AzureDedupTestEvent(grainId, 2) with
        {
            EventId = idY,
            OccurredAt = DateTimeOffset.UtcNow,
        };
        await publisher.PublishAsync(evtX);
        await publisher.PublishAsync(evtY);

        var reactivated = fixture.Cluster.GrainFactory.GetGrain<IAzureDedupTestConsumer>(grainId);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var ids = await reactivated.GetHandledEventIdsAsync();
            if (ids.Contains(idY))
            {
                break;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
        var handled = await reactivated.GetHandledEventIdsAsync();
        Assert.Contains(idY, handled);
        Assert.DoesNotContain(idX, handled);
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
