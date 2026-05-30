using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Orders.Commands;

public sealed partial record PlaceOrderCommand(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid OrderId,
    string CustomerReference) : EdictCommand;
