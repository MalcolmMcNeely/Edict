using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Cart.Commands;

public sealed partial record AddItemToCartCommand(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid CartId,
    [property: EdictTelemeterized] string Sku) : EdictCommand;
