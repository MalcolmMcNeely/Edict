using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Orders.Commands;

/// <param name="Skus">The whole basket the Cart to Order bridge Saga forwards in one Command, so the placed Order carries every checked-out item.</param>
public sealed partial record PlaceOrderFromCartCommand(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid OrderId,
    IReadOnlyList<string> Skus) : EdictCommand;
