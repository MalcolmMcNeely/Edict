using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Watchdog.Events;

[EdictStream("Watchdog")]
public sealed partial record WatchdogEscalatedEvent(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid WatchdogId,
    int PollsObserved) : EdictEvent;
