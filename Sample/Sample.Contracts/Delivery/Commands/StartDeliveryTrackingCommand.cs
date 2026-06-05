using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Delivery.Commands;

public sealed partial record StartDeliveryTrackingCommand(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid OrderId,
    int EtaDays) : EdictCommand;
