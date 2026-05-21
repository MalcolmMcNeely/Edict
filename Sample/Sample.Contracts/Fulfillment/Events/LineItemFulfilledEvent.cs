using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace Sample.Contracts.Fulfillment.Events;

[EdictStream("Fulfillment")]
public sealed partial record LineItemFulfilledEvent(Guid OrderId, Guid LineItemId) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public Guid LineItemId { get; init; } = LineItemId;
}
