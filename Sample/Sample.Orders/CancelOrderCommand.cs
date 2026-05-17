using Edict.Abstractions;

namespace Sample.Orders;

public sealed record CancelOrderCommand(Guid OrderId) : Command
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;
}
