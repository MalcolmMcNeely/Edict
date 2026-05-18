using Edict.Contracts.Commands;
using Edict.Contracts.Events;

using MessagePack;

namespace Sample.Silo.Orders;

[MessagePackObject(keyAsPropertyName: true)]
[Stream("Orders")]
public sealed partial record LineItemAddedEvent(Guid OrderId, string Sku, int Quantity) : Event
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Sku { get; init; } = Sku;
    public int Quantity { get; init; } = Quantity;
}
