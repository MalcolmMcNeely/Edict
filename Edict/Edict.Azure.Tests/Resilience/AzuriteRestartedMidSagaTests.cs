namespace Edict.Azure.Tests.Resilience;

// "Restart" is modelled as docker pause rather than stop+start: a true
// restart re-binds Azurite to a new ephemeral port, invalidating the silo's
// already-configured Azure clients (a host-side wiring failure unrelated to
// what the test proves).
[Collection(ResilienceCollection.Name)]
public sealed class AzuriteRestartedMidSagaTests(ResilienceClusterFixture fixture)
{
    [Fact]
    public async Task SagaHandle_ShouldResumeFromDurableProgress_WhenAzuritePausedMidSaga()
    {
        await fixture.EnsureRunningAsync();

        var workflowId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IResilienceEventPublisher>(workflowId);
        var tracker = fixture.Cluster.GrainFactory.GetGrain<IResilienceSagaTrackerProbe>(workflowId);
        var saga = fixture.Cluster.GrainFactory.GetGrain<IResilienceSagaProgressProbe>(workflowId);

        var triggerEventId = Guid.NewGuid();
        var trigger = new ResilienceSagaTriggerEvent(workflowId) with
        {
            EventId = triggerEventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishSagaTriggerAsync(trigger);

        // Span at least one queue visibility timeout (fixture: 5s) so any
        // in-flight delivery requires redelivery after resume.
        await fixture.PauseAzuriteAsync();
        await Task.Delay(TimeSpan.FromSeconds(7));
        await fixture.UnpauseAzuriteAsync();

        await ResilienceWaiters.WaitForReceivedAsync(tracker);

        Assert.Equal(1, await saga.GetHandledAsync());
        Assert.Equal(1, await tracker.GetReceivedAsync());
        Assert.Equal(workflowId, await tracker.GetLastWorkflowIdAsync());
    }
}
