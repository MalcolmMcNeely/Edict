using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;

using Xunit;

namespace Edict.Core.Tests.Tenancy;

// The single chokepoint every route-key emit site folds through. At this slice it
// returns the bare stringified route key on both axes; the "{tenant}|{guid}" fold
// lands in the next slice, so a tenant-scoped key still composes to its bare form
// here and only the wiring through this one seam is what these pin.
public sealed class EdictKeyComposerTests
{
    [Fact]
    public void Compose_ShouldReturnBareRouteKey_WhenNoTenant()
    {
        var composed = EdictKeyComposer.Compose(null, "9b2c0f5d4e1a4b8c");

        Assert.Equal("9b2c0f5d4e1a4b8c", composed);
    }

    [Fact]
    public void Compose_ShouldReturnBareRouteKey_WhenTenantPresent()
    {
        var composed = EdictKeyComposer.Compose(EdictTenantId.Of("acme-corp"), "9b2c0f5d4e1a4b8c");

        Assert.Equal("9b2c0f5d4e1a4b8c", composed);
    }
}
