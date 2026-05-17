using Edict.Abstractions;

namespace Sample.Orders;

public sealed record AddLineItemCommand(Guid OrderId, string Sku, int Quantity) : Command
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;

    [Telemeterized]
    public string Sku { get; init; } = Sku;

    public int Quantity { get; init; } = Quantity;
}
