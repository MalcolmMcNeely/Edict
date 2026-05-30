using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Diagnostics.Commands;

public sealed partial record TriggerSagaFailureCommand(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid SimulationId) : EdictCommand;
