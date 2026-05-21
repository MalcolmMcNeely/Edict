using Edict.Contracts.Commands;

namespace Sample.Contracts.Orders.Commands;

public sealed partial record MarkOrderShippedCommand(Guid OrderId) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}
