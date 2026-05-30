using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Telemetry;
using Edict.Core.Commands;

using MessagePack;

namespace Edict.Core.Tests;

// Sample-shaped consumer code. The generator runs over this assembly and emits
// IOrderCommandHandler, OrderCommandHandler.Dispatch, the Orleans surrogates and AddEdict().
public sealed partial record PlaceOrderCommand(Guid OrderId, string Sku) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    [EdictTelemeterized]
    public string Sku { get; init; } = Sku;
}

public sealed partial record CancelOrderCommand(Guid OrderId, string Reason) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

public sealed partial record FailOrderCommand(Guid OrderId) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

public sealed partial record ValidateSkuCommand(Guid OrderId, string Sku) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Sku { get; init; } = Sku;
}

public sealed partial record StateCheckCommand(Guid OrderId) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

[EdictStream("Orders")]
public sealed partial record OrderPlacedEvent(Guid OrderId, string Sku) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Sku { get; init; } = Sku;
}

public partial class OrderCommandHandler : EdictCommandHandler
{
    public Task<EdictCommandResult> HandleAsync(PlaceOrderCommand command)
    {
        Raise(new OrderPlacedEvent(command.OrderId, command.Sku));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> HandleAsync(CancelOrderCommand command) =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
            [new EdictRejectionReason("already_shipped", "Order has already shipped.")]));

    public Task<EdictCommandResult> HandleAsync(FailOrderCommand command) =>
        throw new InvalidOperationException("simulated failure");

    public Task<EdictCommandResult> HandleAsync(ValidateSkuCommand command) =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

    public Task<EdictCommandResult> HandleAsync(StateCheckCommand command) =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

    protected override object? GetValidationState() => "grain-active";
}
