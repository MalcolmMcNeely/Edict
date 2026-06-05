using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Delivery.Events;

[EdictStream("Delivery")]
public sealed partial record DeliveredEvent(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid OrderId) : EdictEvent;
