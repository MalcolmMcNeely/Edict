using Edict.Contracts.Commands;

namespace Sample.Contracts.Fulfillment.Commands;

public sealed partial record StartFulfillmentCommand(Guid OrderId, IReadOnlyList<Guid> LineItemIds) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public IReadOnlyList<Guid> LineItemIds { get; init; } = LineItemIds;
}
