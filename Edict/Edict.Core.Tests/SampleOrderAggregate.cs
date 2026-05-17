using Edict.Abstractions;
using Edict.Core;

namespace Edict.Core.Tests;

// Sample-shaped consumer code. The generator runs over this assembly and emits
// IOrderGrain, OrderGrain.Dispatch, the Orleans surrogates and AddEdict().

public sealed record PlaceOrderCommand(Guid OrderId, string Sku) : Command
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;

    [Telemeterized]
    public string Sku { get; init; } = Sku;
}

public sealed record CancelOrderCommand(Guid OrderId, string Reason) : Command
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

public sealed record FailOrderCommand(Guid OrderId) : Command
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

public partial class OrderGrain : CommandHandlerGrain
{
    public Task<CommandResult> Handle(PlaceOrderCommand command) =>
        Task.FromResult<CommandResult>(new CommandResult.Accepted());

    public Task<CommandResult> Handle(CancelOrderCommand command) =>
        Task.FromResult<CommandResult>(new CommandResult.Rejected(
            [new RejectionReason("already_shipped", "Order has already shipped.")]));

    public Task<CommandResult> Handle(FailOrderCommand command) =>
        throw new InvalidOperationException("simulated failure");
}
