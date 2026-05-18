using Edict.Contracts.Commands;

using MessagePack;

namespace Sample.Orders;

[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record CancelOrderCommand(Guid OrderId) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}
