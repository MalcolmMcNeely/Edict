namespace Edict.Azure.Tests.Resilience;

/// <summary>
/// Scenario 2 of the transport-fault suite (issue #96): the substrate goes
/// dark mid-saga and then comes back. Invariant: the saga resumes from its
/// durable Progress state, no double effect is observed by the tracker.
///
/// "Restart" here is modelled as <c>docker pause</c> rather than stop+start
/// because Azurite re-binds to a new ephemeral port after a true restart,
/// which would invalidate the silo's already-configured Azure Queue / Blob
/// clients (a host-side wiring failure unrelated to what the test proves).
/// The pause-window-long-enough-to-cover-the-saga's-state-write semantic
/// matches the issue's invariant: durable state survives substrate downtime
/// and the saga's downstream command is delivered exactly once.
/// </summary>
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

        // Pause Azurite long enough to span at least one Azure Queue
        // visibility timeout (5s — fixture config) so any in-flight delivery
        // requires redelivery after resume. The saga's outbox-effect commit
        // and the tracker's command-state commit both must traverse the
        // substrate-paused window.
        await fixture.PauseAzuriteAsync();
        await Task.Delay(TimeSpan.FromSeconds(7));
        await fixture.UnpauseAzuriteAsync();

        await ResilienceWaiters.WaitForReceivedAsync(tracker);

        Assert.Equal(1, await saga.GetHandledAsync());
        Assert.Equal(1, await tracker.GetReceivedAsync());
        Assert.Equal(workflowId, await tracker.GetLastWorkflowIdAsync());
    }
}
