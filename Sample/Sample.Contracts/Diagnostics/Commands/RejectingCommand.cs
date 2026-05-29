using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Diagnostics.Commands;

public sealed partial record RejectingCommand(Guid SimulationId) : EdictCommand
{
    [EdictRouteKey]
    [EdictTelemeterized]
    public Guid SimulationId { get; init; } = SimulationId;
}
