using Edict.Contracts.Commands;
using Edict.Contracts.Events;

using MessagePack;

namespace Sample.Contracts.Orders.Events;

[MessagePackObject(keyAsPropertyName: true)]
[EdictStream("Orders")]
public sealed partial record OrderConfirmedEvent(Guid OrderId) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}
