using Edict.Abstractions;

using MessagePack;

namespace Sample.Orders;

[MessagePackObject(keyAsPropertyName: true)]
public sealed record CancelOrderCommand(Guid OrderId) : Command
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;
}
