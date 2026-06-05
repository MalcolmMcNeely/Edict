using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Countdown.Commands;

public sealed partial record StartCountdownCommand(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid CountdownId,
    int From) : EdictCommand;
