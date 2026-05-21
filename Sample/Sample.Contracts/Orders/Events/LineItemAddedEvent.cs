using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace Sample.Contracts.Orders.Events;

[EdictStream("Orders")]
public sealed partial record LineItemAddedEvent(Guid OrderId, Guid LineItemId, string Sku, int Quantity) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public Guid LineItemId { get; init; } = LineItemId;
    public string Sku { get; init; } = Sku;
    public int Quantity { get; init; } = Quantity;
}
