namespace Edict.Azure.Tests.Sagas;

/// <summary>
/// Azurite/Testcontainers conformance for the SendCommand outbox
/// effect on a saga: when an event is delivered to <see cref="AzureWorkflowSaga"/>
/// the saga records durable <c>Progress</c>, dispatches exactly one
/// <see cref="AzureSagaTrackerCommand"/>, and the inline outbox drain routes
/// the command to its handler. Lifted from <c>SagaWorkflowTests</c> in
/// Core.Tests so the proof runs on the same Azure Queue + Azure Blob
/// substrate the sample silo wires in production.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class SagaSendCommandEffectDeliversTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task SagaHandle_ShouldDispatchOneCommandAndPersistProgress_WhenEventDelivered()
    {
        var workflowId = Guid.NewGuid();

        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureSagaEventPublisher>(workflowId);
        await publisher.PublishAsync(new AzureSagaTriggerEvent(workflowId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        var tracker = fixture.Cluster.GrainFactory.GetGrain<IAzureSagaTrackerProbe>(workflowId);
        await AzureSagaWaiters.WaitForReceivedAsync(tracker);

        var saga = fixture.Cluster.GrainFactory.GetGrain<IAzureSagaProgressProbe>(workflowId);

        Assert.Equal(1, await tracker.GetReceivedAsync());
        Assert.Equal(workflowId, await tracker.GetLastWorkflowIdAsync());
        Assert.Equal(1, await saga.GetHandledAsync());
    }
}
