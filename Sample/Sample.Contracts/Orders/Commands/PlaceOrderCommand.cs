using Edict.Contracts.Commands;

namespace Sample.Contracts.Orders.Commands;

public sealed partial record PlaceOrderCommand(Guid OrderId, string CustomerReference) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string CustomerReference { get; init; } = CustomerReference;
}
