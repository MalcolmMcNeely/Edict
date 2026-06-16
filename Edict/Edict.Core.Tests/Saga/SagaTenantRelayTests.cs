using Edict.Contracts.Tenancy;

using Xunit;

namespace Edict.Core.Tests.Saga;

// Pushes a trigger event carrying a known tenant onto the saga's stream and reads
// the tenant back off the command the saga dispatched, so the relay is asserted
// through the real dispatch + SendCommand drain. The dispatched command stays
// inside the handled event's tenant wall without the durable field being threaded
// by hand.
[Collection(TenantRelaySagaCollection.Name)]
public sealed class SagaTenantRelayTests(TenantRelaySagaClusterFixture fixture)
{
    [Fact]
    public async Task DispatchedCommand_ShouldInheritHandledEventTenant_AcrossTheSagaHop()
    {
        // Arrange
        var workflowId = Guid.NewGuid();
        var tenant = EdictTenantId.Of("acme");
        var publisher = fixture.GrainFactory.GetGrain<ISpanSagaEventPublisher>(workflowId);

        // Act
        await publisher.PublishAsync(new SpanSagaTriggerEvent(workflowId)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid(),
            Tenant = tenant,
        });

        // Assert
        var tracker = fixture.GrainFactory.GetGrain<ISpanTrackerProbe>(workflowId);
        await SpanSagaWaiters.WaitForReceivedAsync(tracker);
        Assert.Equal(tenant, await tracker.GetLastTenantAsync());
    }
}
