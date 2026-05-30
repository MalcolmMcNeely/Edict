using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Orders.Events;

[EdictStream("Orders")]
public sealed partial record OrderConfirmedEvent(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid OrderId,
    IReadOnlyList<Guid> LineItemIds) : EdictEvent;
