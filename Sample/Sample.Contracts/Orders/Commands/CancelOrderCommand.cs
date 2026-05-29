using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Orders.Commands;

public sealed partial record CancelOrderCommand(Guid OrderId, string Reason) : EdictCommand
{
    [EdictRouteKey]
    [EdictTelemeterized]
    public Guid OrderId { get; init; } = OrderId;

    public string Reason { get; init; } = Reason;
}
