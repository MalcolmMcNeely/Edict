using Edict.Contracts.Commands;

namespace Sample.Contracts.Diagnostics.Commands;

public sealed partial record TriggerSagaFailureCommand(Guid SimulationId) : EdictCommand
{
    [EdictRouteKey]
    public Guid SimulationId { get; init; } = SimulationId;
}
