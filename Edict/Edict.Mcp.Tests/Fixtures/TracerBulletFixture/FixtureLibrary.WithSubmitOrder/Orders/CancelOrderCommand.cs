using Edict.Contracts.Commands;

namespace FixtureLibrary.WithSubmitOrder.Orders;

public sealed partial record CancelOrderCommand : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; }
}
