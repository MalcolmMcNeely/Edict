using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Fulfillment.Commands;

public sealed partial record StartFulfillmentCommand(Guid OrderId, IReadOnlyList<Guid> LineItemIds) : EdictCommand
{
    [EdictRouteKey]
    [EdictTelemeterized]
    public Guid OrderId { get; init; } = OrderId;

    public IReadOnlyList<Guid> LineItemIds { get; init; } = LineItemIds;
}
