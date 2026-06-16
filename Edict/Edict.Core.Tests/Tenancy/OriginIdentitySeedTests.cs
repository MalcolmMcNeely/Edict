using Edict.Contracts.Audit;
using Edict.Contracts.Tenancy;
using Edict.Telemetry;

using Xunit;

namespace Edict.Core.Tests.Tenancy;

// OriginIdentity.Seed is the single seam every grain entry uses to seed the
// per-turn identity relays: it takes the principal and the tenant together, so a
// forgotten tenant seed (a silent cross-tenant leak) is unrepresentable. These
// assert that one call seeds both relays and that a null on either axis clears only
// that slot, leaving the other intact.
public sealed class OriginIdentitySeedTests
{
    [Fact]
    public void Seed_SeedsBothRelays_FromOneCall()
    {
        // Act
        OriginIdentity.Seed(EdictPrincipal.Of("alice"), EdictTenantId.Of("acme"));

        // Assert
        Assert.Equal(EdictPrincipal.Of("alice"), PrincipalRelay.Current());
        Assert.Equal(EdictTenantId.Of("acme"), TenantRelay.Current());
    }

    [Fact]
    public void Seed_WithNullTenant_ClearsTenant_ButKeepsPrincipal()
    {
        // Arrange — a prior turn seeded both.
        OriginIdentity.Seed(EdictPrincipal.Of("alice"), EdictTenantId.Of("acme"));

        // Act — a later turn inherits a principal but no tenant.
        OriginIdentity.Seed(EdictPrincipal.Of("bob"), null);

        // Assert
        Assert.Equal(EdictPrincipal.Of("bob"), PrincipalRelay.Current());
        Assert.Null(TenantRelay.Current());
    }

    [Fact]
    public void Seed_WithNullPrincipal_ClearsPrincipal_ButKeepsTenant()
    {
        // Arrange
        OriginIdentity.Seed(EdictPrincipal.Of("alice"), EdictTenantId.Of("acme"));

        // Act
        OriginIdentity.Seed(null, EdictTenantId.Of("globex"));

        // Assert
        Assert.Null(PrincipalRelay.Current());
        Assert.Equal(EdictTenantId.Of("globex"), TenantRelay.Current());
    }
}
