using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace FixtureLibrary.WithSubmitOrder.Orders;

[EdictStream("Orders")]
public sealed partial record OrderPlaced : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; }
}
