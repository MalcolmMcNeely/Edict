using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Diagnostics.Events;

[EdictStream("Diagnostics")]
public sealed partial record TriggerSagaFailureEvent(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid SimulationId) : EdictEvent;
