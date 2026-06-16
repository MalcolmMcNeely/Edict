using Edict.Contracts.Audit;
using Edict.Contracts.Events;
using Edict.Contracts.Tenancy;
using Edict.Core.Tests;
using Edict.Core.Tests.Serialization;

using MessagePack;

using Xunit;

namespace Edict.Core.Tests.Tenancy;

// The wire-shape snapshots only serialize; the carry depends on the Tenant field
// surviving a full MessagePack deserialize, the path a stream hop takes. These pin
// that round-trip directly, the way EdictPrincipal is implicitly proven by the
// audit suite.
public sealed class TenantRoundTripTests
{
    static readonly Guid AggregateId = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Command_ShouldPreserveTenant_AcrossMessagePackRoundTrip()
    {
        // Arrange
        var command = new PlaceOrderCommand(AggregateId, "ITEM-1")
        {
            Principal = EdictPrincipal.Of("auth0|abc-123"),
            Tenant = EdictTenantId.Of("acme-corp"),
        };

        // Act
        var bytes = MessagePackSerializer.Serialize(command);
        var roundTripped = MessagePackSerializer.Deserialize<PlaceOrderCommand>(bytes);

        // Assert
        Assert.Equal(EdictTenantId.Of("acme-corp"), roundTripped.Tenant);
        Assert.Equal(EdictPrincipal.Of("auth0|abc-123"), roundTripped.Principal);
    }

    [Fact]
    public void Event_ShouldPreserveTenant_AcrossMessagePackRoundTrip()
    {
        // Arrange
        var edictEvent = new OrderPlacedEvent(AggregateId, "ITEM-1")
        {
            Principal = EdictPrincipal.Of("auth0|abc-123"),
            Tenant = EdictTenantId.Of("acme-corp"),
        };

        // Act
        var bytes = MessagePackSerializer.Serialize(edictEvent);
        var roundTripped = MessagePackSerializer.Deserialize<OrderPlacedEvent>(bytes);

        // Assert
        Assert.Equal(EdictTenantId.Of("acme-corp"), roundTripped.Tenant);
        Assert.Equal(EdictPrincipal.Of("auth0|abc-123"), roundTripped.Principal);
    }

}
