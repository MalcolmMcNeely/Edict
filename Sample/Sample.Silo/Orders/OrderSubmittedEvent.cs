using Edict.Contracts.Commands;
using Edict.Contracts.Events;

using MessagePack;

namespace Sample.Silo.Orders;

[MessagePackObject(keyAsPropertyName: true)]
[EdictStream("Orders")]
public sealed partial record OrderSubmittedEvent(Guid OrderId) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}
