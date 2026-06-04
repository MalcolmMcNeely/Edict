using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Cart.Commands;

public sealed partial record CheckoutCartCommand(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid CartId) : EdictCommand;
