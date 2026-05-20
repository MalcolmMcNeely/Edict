using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace Sample.Contracts.Payments.Events;

[EdictStream("Payments")]
public sealed partial record PaymentAuthorizedEvent(Guid OrderId) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}
