using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace Sample.Contracts.Orders.Events;

[EdictStream("Orders")]
public sealed partial record OrderConfirmedEvent(Guid OrderId, IReadOnlyList<Guid> LineItemIds) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public IReadOnlyList<Guid> LineItemIds { get; init; } = LineItemIds;
}
