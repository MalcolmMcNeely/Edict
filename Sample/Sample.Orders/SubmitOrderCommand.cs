using Edict.Abstractions;

namespace Sample.Orders;

public sealed record SubmitOrderCommand(Guid OrderId) : Command
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;
}
