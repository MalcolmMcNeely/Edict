using Edict.Core.Tests.TestSupport;

using Xunit;

namespace Edict.Core.Tests.Saga;

// Pushes a trigger event with a known correlation onto the saga's stream and
// reads the correlation back off the command the saga dispatched, so the
// event-to-command hop is asserted through the real dispatch + SendCommand drain
// — proving a chain survives a Saga in the middle, where a per-hop EventId would
// not.
[Collection(SubstrateIndependentCollection.Name)]
public sealed class SagaCorrelationPropagationTests(SubstrateIndependentClusterFixture fixture)
{
    [Fact]
    public async Task DispatchedCommand_ShouldInheritHandledEventCorrelation_AcrossTheSagaHop()
    {
        // Arrange
        var workflowId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var publisher = fixture.GrainFactory.GetGrain<ISpanSagaEventPublisher>(workflowId);

        // Act
        await publisher.PublishAsync(new SpanSagaTriggerEvent(workflowId)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            ConversationId = correlationId,
        });

        // Assert
        var tracker = fixture.GrainFactory.GetGrain<ISpanTrackerProbe>(workflowId);
        await SpanSagaWaiters.WaitForReceivedAsync(tracker);
        Assert.Equal(correlationId, await tracker.GetLastCorrelationIdAsync());
    }
}
