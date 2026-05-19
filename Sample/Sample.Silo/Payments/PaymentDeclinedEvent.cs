using Edict.Contracts.Commands;
using Edict.Contracts.Events;

using MessagePack;

namespace Sample.Silo.Payments;

[MessagePackObject(keyAsPropertyName: true)]
[EdictStream("Payments")]
public sealed partial record PaymentDeclinedEvent(Guid OrderId) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}
