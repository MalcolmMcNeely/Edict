using Edict.Contracts.Commands;

namespace FixtureLibrary.WithSubmitOrder.Orders;

public sealed partial record PlaceOrderCommand : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; }
}
