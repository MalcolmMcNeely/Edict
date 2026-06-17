using Edict.Contracts.Tenancy;
using Edict.Core.Commands;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Edict.Core.Tests.Tenancy;

// A deliberately un-[EdictTenantScoped] aggregate is the visible fleet-wide
// opt-out: its route key composes to the bare guid, so every tenant's traffic
// lands on one shared grain. The fold is a property of the route-key type, never
// of whichever tenant a hop happened to carry — so even a command stamped with a
// tenant (a tenant->public establishing crossing) keys to the bare guid here.
public sealed class PublicAggregateOptOutTests
{
    [Fact]
    public void PublicCommand_ShouldComposeBareKey_EvenWhenATenantRidesTheMessage()
    {
        // Arrange: the real generated route for the public PlaceOrderCommand.
        var routes = RouteDiscovery.Discover(
            [typeof(PlaceOrderCommand).Assembly], requireAttribute: false, NullLogger.Instance);
        var route = routes[typeof(PlaceOrderCommand)];
        var orderId = Guid.NewGuid();

        // Act: a tenant rides the command, but the aggregate is public.
        var key = route.RouteKeySelector(
            new PlaceOrderCommand(orderId, "SKU-1") { Tenant = EdictTenantId.Of("acme") });

        // Assert: the bare key — no wall folded in.
        Assert.Equal(orderId.ToString("N"), key);
    }
}
