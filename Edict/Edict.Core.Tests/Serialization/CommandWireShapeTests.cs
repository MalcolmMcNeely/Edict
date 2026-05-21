using Edict.Contracts.Commands;

using MessagePack;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Serialization;

// Codec-breadth probe: spans every primitive the contract codec must carry —
// Guid, string, int, bool, double, DateTimeOffset and a nullable string. The
// order commands (Guid + string only) do not exercise this breadth.
public sealed partial record MixedPrimitiveCommand(Guid ProbeId) : EdictCommand
{
    [EdictRouteKey]
    public Guid ProbeId { get; init; } = ProbeId;

    public string Label { get; init; } = "";

    public int Count { get; init; }

    public bool Flag { get; init; }

    public double Ratio { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public string? Note { get; init; }
}

public sealed class CommandWireShapeTests
{
    private static readonly Guid FixedCommandId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FixedAggregateId = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public Task PlaceOrderCommand_ShouldHaveStableWireShape()
    {
        var command = new PlaceOrderCommand(FixedAggregateId, "ITEM-1")
        {
            CommandId = FixedCommandId,
        };
        return VerifyWireShape(command);
    }

    [Fact]
    public Task CancelOrderCommand_ShouldHaveStableWireShape()
    {
        var command = new CancelOrderCommand(FixedAggregateId, "customer-request")
        {
            CommandId = FixedCommandId,
        };
        return VerifyWireShape(command);
    }

    [Fact]
    public Task FailOrderCommand_ShouldHaveStableWireShape()
    {
        var command = new FailOrderCommand(FixedAggregateId)
        {
            CommandId = FixedCommandId,
        };
        return VerifyWireShape(command);
    }

    [Fact]
    public Task ValidateSkuCommand_ShouldHaveStableWireShape()
    {
        var command = new ValidateSkuCommand(FixedAggregateId, "SKU-001")
        {
            CommandId = FixedCommandId,
        };
        return VerifyWireShape(command);
    }

    [Fact]
    public Task StateCheckCommand_ShouldHaveStableWireShape()
    {
        var command = new StateCheckCommand(FixedAggregateId)
        {
            CommandId = FixedCommandId,
        };
        return VerifyWireShape(command);
    }

    [Fact]
    public Task MixedPrimitiveCommand_ShouldHaveStableWireShape()
    {
        var command = new MixedPrimitiveCommand(FixedAggregateId)
        {
            CommandId = FixedCommandId,
            Label = "probe-label",
            Count = 42,
            Flag = true,
            Ratio = 3.5,
            OccurredAt = new DateTimeOffset(2026, 5, 17, 9, 30, 0, TimeSpan.FromHours(1)),
            Note = "nullable-present",
        };
        return VerifyWireShape(command);
    }

    private static Task VerifyWireShape<T>(T command) where T : EdictCommand
    {
        var bytes = MessagePackSerializer.Serialize(command);
        var json = MessagePackSerializer.ConvertToJson(bytes);
        return Verify(json);
    }
}
