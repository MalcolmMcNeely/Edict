using System.Diagnostics;

using Edict.Contracts.Sending;
using Edict.Contracts.Tenancy;
using Edict.Core.Commands;
using Edict.Core.Tenancy;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Edict.Core.Tests.Tenancy;

// The relayed fail-closed gate (the composition-site backstop). A relayed send — a
// saga Dispatch — is exempt from the origin stamper's gate, so a tenant-scoped target
// reached without a tenant would otherwise compose a bare key and co-mingle its state
// in the shared default partition. Establishing a crossing stays origin-only; the
// relay must fail closed instead of guessing a wall.
[Collection(RelayTenantGateCollection.Name)]
public sealed class RelayTenantGateTests(RelayTenantGateClusterFixture fixture)
{
    [Fact]
    public async Task SagaDispatchingTenantNullEventIntoWalledAggregate_ShouldDeadLetter_NotLandABareKey()
    {
        // Arrange — a trigger carrying no tenant: the public-to-tenant crossing the gate refuses.
        var workflowId = Guid.NewGuid();
        var publisher = fixture.GrainFactory.GetGrain<IRelayGatePublisher>(workflowId);

        // Act
        await publisher.PublishAsync(new CrossIntoWalledTrigger(workflowId)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            ConversationId = Guid.NewGuid(),
        });

        // Assert — the relayed dispatch dead-letters as a missing-tenant fault, and the
        // walled target never sees the command, so no bare key landed.
        var deadLetter = await RelayTenantGateWaiters.WaitForDeadLetterAsync(
            raised => raised.ExceptionType is not null
                && raised.ExceptionType.Contains(nameof(EdictMissingTenantException), StringComparison.Ordinal));

        Assert.NotNull(deadLetter);
        Assert.DoesNotContain(
            RelayTenantGateCaptures.WalledTargetReceived,
            command => command.TargetId.Value == workflowId);
    }

    [Fact]
    public async Task SagaDispatchingTenantBearingEventIntoWalledAggregate_ShouldLandUnderThatTenant()
    {
        // Arrange — an intra-tenant relay: the trigger carries a tenant the dispatch inherits.
        var workflowId = Guid.NewGuid();
        var tenant = EdictTenantId.Of("acme");
        var publisher = fixture.GrainFactory.GetGrain<IRelayGatePublisher>(workflowId);

        // Act
        await publisher.PublishAsync(new CrossIntoWalledTrigger(workflowId)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            ConversationId = Guid.NewGuid(),
            Tenant = tenant,
        });

        // Assert — the dispatch passes the gate and lands carrying the inherited tenant.
        var landed = await RelayTenantGateWaiters.WaitForWalledTargetAsync(workflowId);

        Assert.NotNull(landed);
        Assert.Equal(tenant, landed.Tenant);
    }

    [Fact]
    public async Task SagaDispatchingTenantNullEventIntoPublicAggregate_ShouldLandUnchanged()
    {
        // Arrange — a public target: a null relayed tenant composes a bare key and the gate stays out of the way.
        var workflowId = Guid.NewGuid();
        var publisher = fixture.GrainFactory.GetGrain<IRelayGatePublisher>(workflowId);

        // Act
        await publisher.PublishAsync(new CrossIntoPublicTrigger(workflowId)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            ConversationId = Guid.NewGuid(),
        });

        // Assert
        var landed = await RelayTenantGateWaiters.WaitForPublicTargetAsync(workflowId);

        Assert.NotNull(landed);
        Assert.Null(landed.Tenant);
    }

    [Fact]
    public async Task FastPathSend_ShouldRefuseTenantScopedTargetWithNullTenant_BeforeComposingAKey()
    {
        // Arrange — the generator fast path, exempt from the origin stamper via the
        // cross-turn-link marker, so the composition-site gate is the only throw.
        var sender = (EdictSender)fixture.Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();
        ActivityExtensions.MarkCrossTurnLink();
        try
        {
            // Act + Assert
            await Assert.ThrowsAsync<EdictMissingTenantException>(() =>
                sender.SendFastPathAsync(
                    new DispatchIntoWalledTarget(new WalledTargetId(Guid.NewGuid())),
                    routeKey: Guid.NewGuid().ToString("N"),
                    tenantScoped: true,
                    commandSimpleName: nameof(DispatchIntoWalledTarget),
                    grainClassName: "WalledTargetCommandHandler",
                    extraTags: (Action<DispatchIntoWalledTarget, Activity>?)null));
        }
        finally
        {
            ActivityExtensions.ClearCrossTurnLink();
        }
    }
}
