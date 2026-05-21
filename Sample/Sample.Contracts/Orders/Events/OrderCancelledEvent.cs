using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace Sample.Contracts.Orders.Events;

[EdictStream("Orders")]
public sealed partial record OrderCancelledEvent(Guid OrderId, string Reason) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Reason { get; init; } = Reason;
}
