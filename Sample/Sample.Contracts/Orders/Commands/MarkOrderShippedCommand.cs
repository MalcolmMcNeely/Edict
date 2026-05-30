using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Orders.Commands;

public sealed partial record MarkOrderShippedCommand(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid OrderId) : EdictCommand;
