using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Watchdog.Commands;

public sealed partial record StartWatchdogCommand(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid WatchdogId) : EdictCommand;
