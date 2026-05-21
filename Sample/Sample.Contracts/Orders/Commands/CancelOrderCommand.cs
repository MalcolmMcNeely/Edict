using Edict.Contracts.Commands;

namespace Sample.Contracts.Orders.Commands;

public sealed partial record CancelOrderCommand(Guid OrderId, string Reason) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Reason { get; init; } = Reason;
}
