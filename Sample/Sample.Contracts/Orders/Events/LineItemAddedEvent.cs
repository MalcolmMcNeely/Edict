using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Orders.Events;

[EdictStream("Orders")]
public sealed partial record LineItemAddedEvent(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid OrderId,
    Guid LineItemId,
    string Sku,
    int Quantity) : EdictEvent;
