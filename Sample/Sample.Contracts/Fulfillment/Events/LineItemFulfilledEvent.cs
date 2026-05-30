using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Fulfillment.Events;

[EdictStream("Fulfillment")]
public sealed partial record LineItemFulfilledEvent(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid OrderId,
    Guid LineItemId) : EdictEvent;
