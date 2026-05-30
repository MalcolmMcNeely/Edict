using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Fulfillment.Commands;

public sealed partial record StartFulfillmentCommand(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid OrderId,
    IReadOnlyList<Guid> LineItemIds) : EdictCommand;
