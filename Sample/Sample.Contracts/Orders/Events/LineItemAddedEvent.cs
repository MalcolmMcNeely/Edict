using Edict.Contracts.Commands;
using Edict.Contracts.Events;

using MessagePack;

namespace Sample.Contracts.Orders.Events;

[MessagePackObject(keyAsPropertyName: true)]
[EdictStream("Orders")]
public sealed partial record LineItemAddedEvent(Guid OrderId, string Sku, int Quantity) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Sku { get; init; } = Sku;
    public int Quantity { get; init; } = Quantity;
}
