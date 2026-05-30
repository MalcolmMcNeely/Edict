using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace FixtureLibrary.Orders;

[EdictStream("Orders")]
public sealed record OrderPlaced : EdictEvent
{
    [EdictRouteKey]
    public System.Guid OrderId { get; init; }
}
