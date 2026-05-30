using Edict.Contracts.Commands;

namespace FixtureLibrary.Orders;

public sealed record CancelOrderCommand : EdictCommand
{
    [EdictRouteKey]
    public System.Guid OrderId { get; init; }
}
