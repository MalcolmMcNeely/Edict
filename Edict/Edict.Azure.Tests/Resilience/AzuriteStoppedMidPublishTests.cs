namespace Edict.Azure.Tests.Resilience;

/// <summary>
/// Scenario 1 of the transport-fault suite (issue #96): Azurite becomes
/// unreachable mid-flow and then comes back. Invariant: no event is lost,
/// redelivery succeeds, and the consumer's effect is applied exactly once.
///
/// The flow publishes an event to the queue (so the message is durably on
/// Azurite's disk) and then pauses Azurite. While paused the pulling agent's
/// next poll hangs; once Azurite is unpaused Orleans reconnects on the same
/// host port, the message is dequeued, and the dedup ring commits.
/// </summary>
[Collection(ResilienceCollection.Name)]
public sealed class AzuriteStoppedMidPublishTests(ResilienceClusterFixture fixture)
{
    [Fact]
    public async Task HandleAsync_ShouldDeliverExactlyOnce_WhenAzuritePausedAfterPublishThenResumed()
    {
        await fixture.EnsureRunningAsync();

        var aggregateId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IResilienceEventPublisher>(aggregateId);
        var consumer = fixture.Cluster.GrainFactory.GetGrain<IResilienceTestConsumer>(aggregateId);

        var evt = new ResilienceTestEvent(aggregateId, 1) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishEventAsync(evt);

        // Pause Azurite — depending on timing the message may already be
        // mid-dispatch, but at minimum the next pulling-agent poll, the
        // consumer's dedup-state commit, or both will hang on the substrate.
        await fixture.PauseAzuriteAsync();
        await Task.Delay(TimeSpan.FromSeconds(3));

        await fixture.UnpauseAzuriteAsync();

        var handled = await ResilienceWaiters.WaitForHandledAsync(consumer);
        Assert.Single(handled);
        Assert.Equal(evt.EventId, handled[0]);
    }
}
