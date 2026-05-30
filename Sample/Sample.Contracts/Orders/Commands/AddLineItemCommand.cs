using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Orders.Commands;

public sealed partial record AddLineItemCommand(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid OrderId,
    Guid LineItemId,
    [property: EdictTelemeterized] string Sku,
    int Quantity) : EdictCommand;
