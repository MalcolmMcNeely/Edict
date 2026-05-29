using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Payments.Events;

[EdictStream("Payments")]
public sealed partial record PaymentAuthorizedEvent(Guid OrderId) : EdictEvent
{
    [EdictRouteKey]
    [EdictTelemeterized]
    public Guid OrderId { get; init; } = OrderId;
}
