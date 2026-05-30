using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Diagnostics.Commands;

public sealed partial record RejectingCommand(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid SimulationId) : EdictCommand;
