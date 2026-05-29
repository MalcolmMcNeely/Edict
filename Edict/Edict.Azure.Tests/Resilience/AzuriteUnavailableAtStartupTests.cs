namespace Edict.Azure.Tests.Resilience;

[Collection(ResilienceCollection.Name)]
public sealed class AzuriteUnavailableAtStartupTests(ResilienceClusterFixture fixture)
{
    [Fact]
    public async Task PublishAndHandle_ShouldConverge_WhenAzuriteUnavailableAtFirstAttempt()
    {
        await fixture.EnsureRunningAsync();

        var aggregateId = Guid.NewGuid();
        var consumer = fixture.Cluster.GrainFactory.GetGrain<IResilienceTestConsumer>(aggregateId);
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IResilienceEventPublisher>(aggregateId);

        var edictEvent = new ResilienceTestEvent(aggregateId, 1) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        // Pausing before the first substrate-touching operation makes the
        // silo's grain activation and stream publish both observe a downed
        // substrate the moment they reach for it.
        await fixture.PauseAzuriteAsync();

        // Detached task so the test can unpause while PublishAsync is stuck
        // inside the grain's Azure Queue write.
        var publishTask = Task.Run(() => publisher.PublishEventAsync(edictEvent));

        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.False(publishTask.IsCompleted,
            "Publish should not complete while Azurite is paused.");

        await fixture.UnpauseAzuriteAsync();

        await publishTask.WaitAsync(TimeSpan.FromSeconds(60));

        var handled = await ResilienceWaiters.WaitForHandledAsync(consumer);
        Assert.Single(handled);
        Assert.Equal(edictEvent.EventId, handled[0]);
    }
}
