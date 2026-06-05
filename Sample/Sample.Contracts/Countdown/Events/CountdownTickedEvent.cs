using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Countdown.Events;

[EdictStream("Countdown")]
public sealed partial record CountdownTickedEvent(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid CountdownId,
    int Remaining) : EdictEvent;
