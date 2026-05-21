using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace Sample.Contracts.Fulfillment.Events;

[EdictStream("Fulfillment")]
public sealed partial record OrderFullyFulfilledEvent(Guid OrderId) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}
