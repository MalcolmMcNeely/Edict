using Edict.Contracts.Commands;
using Edict.Contracts.Events;

using MessagePack;

namespace Sample.Silo.Orders;

[MessagePackObject(keyAsPropertyName: true)]
[Stream("Orders")]
public sealed partial record OrderCancelledEvent(Guid OrderId) : Event
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;
}
