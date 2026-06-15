using Edict.Contracts.Audit;

using Xunit;

namespace Edict.Core.Tests.Saga;

// Pushes a trigger event carrying a known principal onto the saga's stream and
// reads the principal back off the command the saga dispatched, so the relay is
// asserted through the real dispatch + SendCommand drain. Auditing is on with a
// resolver that yields nobody: the dispatched command carries the principal from
// the relay, not the resolver, and the origin fail-closed gate stays untriggered.
[Collection(PrincipalRelaySagaCollection.Name)]
public sealed class SagaPrincipalRelayTests(PrincipalRelaySagaClusterFixture fixture)
{
    [Fact]
    public async Task DispatchedCommand_ShouldInheritHandledEventPrincipal_AcrossTheSagaHop()
    {
        // Arrange
        var workflowId = Guid.NewGuid();
        var principal = EdictPrincipal.Of("alice");
        var publisher = fixture.GrainFactory.GetGrain<ISpanSagaEventPublisher>(workflowId);

        // Act
        await publisher.PublishAsync(new SpanSagaTriggerEvent(workflowId)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid(),
            Principal = principal,
        });

        // Assert
        var tracker = fixture.GrainFactory.GetGrain<ISpanTrackerProbe>(workflowId);
        await SpanSagaWaiters.WaitForReceivedAsync(tracker);
        Assert.Equal(principal, await tracker.GetLastPrincipalAsync());
    }
}
