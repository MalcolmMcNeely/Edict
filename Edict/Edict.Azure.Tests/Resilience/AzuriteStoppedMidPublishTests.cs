namespace Edict.Azure.Tests.Resilience;

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

        await fixture.PauseAzuriteAsync();
        await Task.Delay(TimeSpan.FromSeconds(3));

        await fixture.UnpauseAzuriteAsync();

        var handled = await ResilienceWaiters.WaitForHandledAsync(consumer);
        Assert.Single(handled);
        Assert.Equal(evt.EventId, handled[0]);
    }
}
