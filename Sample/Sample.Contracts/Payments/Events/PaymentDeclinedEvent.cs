using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Payments.Events;

[EdictStream("Payments")]
public sealed partial record PaymentDeclinedEvent(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid OrderId) : EdictEvent;
