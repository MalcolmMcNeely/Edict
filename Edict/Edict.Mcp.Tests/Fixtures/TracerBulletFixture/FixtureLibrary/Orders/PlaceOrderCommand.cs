using Edict.Contracts.Commands;

namespace FixtureLibrary.Orders;

public sealed record PlaceOrderCommand : EdictCommand
{
    [EdictRouteKey]
    public System.Guid OrderId { get; init; }
}
