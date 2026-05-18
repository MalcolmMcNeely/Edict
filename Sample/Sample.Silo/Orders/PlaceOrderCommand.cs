using Edict.Contracts.Commands;

using MessagePack;

namespace Sample.Silo.Orders;

[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record PlaceOrderCommand(Guid OrderId) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}
