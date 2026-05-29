using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Orders.Commands;

public sealed partial record PlaceOrderCommand(Guid OrderId, string CustomerReference) : EdictCommand
{
    [EdictRouteKey]
    [EdictTelemeterized]
    public Guid OrderId { get; init; } = OrderId;

    public string CustomerReference { get; init; } = CustomerReference;
}
