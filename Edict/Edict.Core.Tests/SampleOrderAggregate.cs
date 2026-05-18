using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Results;
using Edict.Contracts.Telemetry;
using Edict.Core.Grains;
using Edict.Core.Tests.Serialization;

using MessagePack;

namespace Edict.Core.Tests;

// Sample-shaped consumer code. The generator runs over this assembly and emits
// IOrderGrain, OrderGrain.Dispatch, the Orleans surrogates and AddEdict().

[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record PlaceOrderCommand(Guid OrderId, string Sku) : Command
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;

    [Telemeterized]
    public string Sku { get; init; } = Sku;
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record CancelOrderCommand(Guid OrderId, string Reason) : Command
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record FailOrderCommand(Guid OrderId) : Command
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

// Commands used by the Command Validator tests (issue #12).

[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record ValidateSkuCommand(Guid OrderId, string Sku) : Command
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Sku { get; init; } = Sku;
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record StateCheckCommand(Guid OrderId) : Command
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

// The domain event raised when a PlaceOrderCommand is accepted.
[MessagePackObject(keyAsPropertyName: true)]
[Stream("Orders")]
public sealed partial record OrderPlacedEvent(Guid OrderId, string Sku) : Event
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Sku { get; init; } = Sku;
}

public partial class OrderGrain : CommandHandlerGrain
{
    public Task<CommandResult> Handle(PlaceOrderCommand command)
    {
        CommandRoundTripRecorder.Record(command);
        Raise(new OrderPlacedEvent(command.OrderId, command.Sku));
        return Task.FromResult<CommandResult>(new CommandResult.Accepted());
    }

    public Task<CommandResult> Handle(CancelOrderCommand command)
    {
        CommandRoundTripRecorder.Record(command);
        return Task.FromResult<CommandResult>(new CommandResult.Rejected(
            [new RejectionReason("already_shipped", "Order has already shipped.")]));
    }

    public Task<CommandResult> Handle(FailOrderCommand command) =>
        throw new InvalidOperationException("simulated failure");

    public Task<CommandResult> Handle(ValidateSkuCommand command) =>
        Task.FromResult<CommandResult>(new CommandResult.Accepted());

    public Task<CommandResult> Handle(StateCheckCommand command) =>
        Task.FromResult<CommandResult>(new CommandResult.Accepted());

    protected override object? GetValidationState() => "grain-active";
}
