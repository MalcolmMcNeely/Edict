using Edict.Contracts.Tenancy;
using Edict.Core.Tests.Grains;

using Xunit;

namespace Edict.Core.Tests.Tenancy;

// Drives a command through the real client sender + engine under tenancy, then
// reads the tenant off the event raised on the command turn — the wall a consumer
// would see on the wire, never an internal field. Proves a tenant survives an edge
// send (the carry acceptance) and that the explicit establishing-crossing overload
// wins over the resolver.
[Collection(TenantOriginCollection.Name)]
public sealed class TenantOriginStampingTests(TenantOriginClusterFixture fixture)
{
    static readonly Guid CounterId = new("cccccccc-0000-0000-0000-000000000001");

    [Fact]
    public async Task SendAsync_ShouldStampResolvedTenant_OntoTheRaisedEvent()
    {
        // Arrange
        TenantCapturingExecutor.Reset();
        TenantSource.Current = EdictTenantId.Of("acme");

        // Act
        await fixture.Sender.SendAsync(new IncrementCounterCommand(CounterId));

        // Assert
        var published = Assert.Single(TenantCapturingExecutor.Captured);
        Assert.Equal(EdictTenantId.Of("acme"), published.Tenant);
    }

    [Fact]
    public async Task SendAsync_WithExplicitTenant_ShouldStampThatTenant_BypassingTheResolver()
    {
        // Arrange — the resolver would supply nobody; the explicit tenant wins.
        TenantCapturingExecutor.Reset();
        TenantSource.Current = null;

        // Act
        await fixture.Sender.SendAsync(new IncrementCounterCommand(CounterId), EdictTenantId.Of("globex"));

        // Assert
        var published = Assert.Single(TenantCapturingExecutor.Captured);
        Assert.Equal(EdictTenantId.Of("globex"), published.Tenant);
    }
}
