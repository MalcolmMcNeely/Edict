using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Orders.Commands;

public sealed partial record AddLineItemCommand(Guid OrderId, string Sku, int Quantity) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    [EdictTelemeterized]
    public string Sku { get; init; } = Sku;

    public int Quantity { get; init; } = Quantity;
}
