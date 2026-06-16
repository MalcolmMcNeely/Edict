using Edict.Contracts.Tenancy;
using Edict.Core.Tenancy;
using Edict.Core.Tests.Grains;

using Xunit;

namespace Edict.Core.Tests.Tenancy;

// The origin-stamping decision in isolation, the four branches a cluster test
// cannot pin cheaply: tenancy off leaves the single-tenant path untaxed; an
// already-present tenant wins; a resolved tenant is stamped; a genuine origin that
// resolves nobody fails closed. The cross-turn-link exemption is exercised through
// the real saga relay test, not here.
public sealed class EdictTenantStamperTests
{
    static readonly Guid CounterId = new("bbbbbbbb-0000-0000-0000-000000000001");

    [Fact]
    public void StampAtOrigin_WhenTenancyOff_LeavesCommandUntaxed()
    {
        // Arrange — a single-tenant app registers no resolver and no marker.
        var stamper = new EdictTenantStamper(tenantEnabled: false, resolver: null);
        var command = new IncrementCounterCommand(CounterId);

        // Act
        var stamped = stamper.StampAtOrigin(command);

        // Assert
        Assert.Null(stamped.Tenant);
    }

    [Fact]
    public void StampAtOrigin_WhenTenantAlreadyPresent_HonoursIt()
    {
        // Arrange — the resolver would supply a different tenant; the present one wins.
        var stamper = new EdictTenantStamper(tenantEnabled: true, resolver: new StubResolver(EdictTenantId.Of("globex")));
        var command = new IncrementCounterCommand(CounterId) { Tenant = EdictTenantId.Of("acme") };

        // Act
        var stamped = stamper.StampAtOrigin(command);

        // Assert
        Assert.Equal(EdictTenantId.Of("acme"), stamped.Tenant);
    }

    [Fact]
    public void StampAtOrigin_WhenTenancyOnAndResolved_StampsTheResolvedTenant()
    {
        // Arrange
        var stamper = new EdictTenantStamper(tenantEnabled: true, resolver: new StubResolver(EdictTenantId.Of("acme")));
        var command = new IncrementCounterCommand(CounterId);

        // Act
        var stamped = stamper.StampAtOrigin(command);

        // Assert
        Assert.Equal(EdictTenantId.Of("acme"), stamped.Tenant);
    }

    [Fact]
    public void StampAtOrigin_WhenTenancyOnAndResolverYieldsNobody_FailsClosed()
    {
        // Arrange
        var stamper = new EdictTenantStamper(tenantEnabled: true, resolver: new StubResolver(null));
        var command = new IncrementCounterCommand(CounterId);

        // Act + Assert
        Assert.Throws<EdictMissingTenantException>(() => stamper.StampAtOrigin(command));
    }

    sealed class StubResolver(EdictTenantId? tenant) : IEdictTenantResolver
    {
        public EdictTenantId? Resolve() => tenant;
    }
}
