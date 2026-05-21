using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace Sample.Contracts.Diagnostics.Events;

[EdictStream("Diagnostics")]
public sealed partial record TriggerSagaFailureEvent(Guid SimulationId) : EdictEvent
{
    [EdictRouteKey]
    public Guid SimulationId { get; init; } = SimulationId;
}
