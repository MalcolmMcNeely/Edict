using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Results;
using Edict.Contracts.Telemetry;
using Edict.Core.Commands;
using Edict.Core.Tests.Serialization;

using MessagePack;

namespace Edict.Core.Tests;

// Sample-shaped consumer code. The generator runs over this assembly and emits
// IOrderGrain, OrderGrain.Dispatch, the Orleans surrogates and AddEdict().

[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record PlaceOrderCommand(Guid OrderId, string Sku) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    [EdictTelemeterized]
    public string Sku { get; init; } = Sku;
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record CancelOrderCommand(Guid OrderId, string Reason) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record FailOrderCommand(Guid OrderId) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

// Commands used by the Command Validator tests (issue #12).

[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record ValidateSkuCommand(Guid OrderId, string Sku) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Sku { get; init; } = Sku;
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record StateCheckCommand(Guid OrderId) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

// The domain event raised when a PlaceOrderCommand is accepted.
[MessagePackObject(keyAsPropertyName: true)]
[EdictStream("Orders")]
public sealed partial record OrderPlacedEvent(Guid OrderId, string Sku) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Sku { get; init; } = Sku;
}

// Test event for EventDeduplicationGrain integration tests.
[MessagePackObject(keyAsPropertyName: true)]
[EdictStream("DedupTest")]
public sealed partial record DedupTestEvent(Guid AggregateId, int Sequence) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;

    public int Sequence { get; init; } = Sequence;
}

public partial class OrderGrain : EdictCommandHandlerGrain
{
    public Task<EdictCommandResult> Handle(PlaceOrderCommand command)
    {
        CommandRoundTripRecorder.Record(command);
        Raise(new OrderPlacedEvent(command.OrderId, command.Sku));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> Handle(CancelOrderCommand command)
    {
        CommandRoundTripRecorder.Record(command);
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
            [new EdictRejectionReason("already_shipped", "Order has already shipped.")]));
    }

    public Task<EdictCommandResult> Handle(FailOrderCommand command) =>
        throw new InvalidOperationException("simulated failure");

    public Task<EdictCommandResult> Handle(ValidateSkuCommand command) =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

    public Task<EdictCommandResult> Handle(StateCheckCommand command) =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

    protected override object? GetValidationState() => "grain-active";
}
