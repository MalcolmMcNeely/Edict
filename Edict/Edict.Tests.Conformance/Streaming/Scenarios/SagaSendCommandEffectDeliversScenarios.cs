using Edict.Tests.Conformance.Sagas;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis conformance for the SendCommand outbox effect on a saga: when
/// an event is delivered over the real stream to <see cref="WorkflowSaga"/>, the
/// saga records durable <c>Progress</c> (held by the reference persistence),
/// dispatches exactly one <see cref="SagaTrackerCommand"/>, and the inline outbox
/// drain routes the command to its handler.
/// </summary>
public abstract class SagaSendCommandEffectDeliversScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected SagaSendCommandEffectDeliversScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SagaHandle_ShouldDispatchOneCommandAndPersistProgress_WhenEventDelivered()
    {
        var workflowId = Guid.NewGuid();

        var publisher = _fixture.GrainFactory.GetGrain<ISagaEventPublisher>(workflowId);
        await publisher.PublishAsync(new SagaTriggerEvent(workflowId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        var tracker = _fixture.GrainFactory.GetGrain<ISagaTrackerProbe>(workflowId);
        await SagaWaiters.WaitForReceivedAsync(tracker);

        var saga = _fixture.GrainFactory.GetGrain<ISagaProgressProbe>(workflowId);

        Assert.Equal(1, await tracker.GetReceivedAsync());
        Assert.Equal(workflowId, await tracker.GetLastWorkflowIdAsync());
        Assert.Equal(1, await saga.GetHandledAsync());
    }
}
