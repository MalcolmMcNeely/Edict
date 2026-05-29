using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Diagnostics.Commands;

public sealed partial record TriggerSagaFailureCommand(Guid SimulationId) : EdictCommand
{
    [EdictRouteKey]
    [EdictTelemeterized]
    public Guid SimulationId { get; init; } = SimulationId;
}
