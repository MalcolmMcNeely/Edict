using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Countdown.Events;

[EdictStream("Countdown")]
public sealed partial record CountdownFinishedEvent(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid CountdownId) : EdictEvent;
