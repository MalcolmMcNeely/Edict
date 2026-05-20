using System.Collections.Concurrent;

using Edict.Contracts.Commands;
using Edict.Contracts.Sending;
using Edict.Core.Commands;

using MessagePack;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Serialization;

// ADR 0007 M1 production proof, superseding the #9 go/no-go spike. Every
// concrete command, sent through IEdictSender.Send(Command) typed as the
// abstract base with NO [Union] on the base or subtype, must round-trip
// across a real Orleans grain hop on the MessagePack serializer with its
// concrete runtime type and every property value intact. Each test pins the
// round-tripped command with a single Verify snapshot — this doubles as the
// contract drift guard ADR 0007 mandates (a renamed/removed property fails
// CI on the snapshot diff). Inputs are fixed, not random, so the snapshot is
// deterministic and the literal values are the round-trip assertion.

// A consumer command spanning every primitive the contract codec must carry:
// Guid, string, int, bool, double, DateTimeOffset and a nullable string. This
// is the codec-breadth case the order commands (Guid + string only) do not
// exercise — it folds in the rigor the #9 spike's probe command proved.
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

public partial class MixedPrimitiveGrain : Edict.Core.Commands.EdictCommandHandler
{
    public Task<EdictCommandResult> Handle(MixedPrimitiveCommand command)
    {
        CommandRoundTripRecorder.Record(command);
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

// Single-process in-memory TestCluster: a static sink keyed by CommandId is
// the simplest way for the receiving grain to surface what it actually
// deserialized so the test can snapshot it from the sender side. Works for
// any Command — every Command carries a CommandId.
public static class CommandRoundTripRecorder
{
    private static readonly ConcurrentDictionary<Guid, EdictCommand> Received = new();

    public static void Record(EdictCommand command) => Received[command.CommandId] = command;

    public static EdictCommand Get(Guid commandId) => Received[commandId];
}

[Collection(EdictClusterCollection.Name)]
public sealed class CommandMessagePackRoundTrip(EdictClusterFixture fixture)
{
    // Distinct per test: the recorder is a process-wide static keyed by
    // CommandId, so a shared id would let one test read another's command.
    private static readonly Guid PlaceCommandId = new("11111111-1111-1111-1111-111111111101");
    private static readonly Guid CancelCommandId = new("11111111-1111-1111-1111-111111111102");
    private static readonly Guid MixedCommandId = new("11111111-1111-1111-1111-111111111103");
    private static readonly Guid FailCommandId = new("11111111-1111-1111-1111-111111111104");
    private static readonly Guid FixedAggregateId = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task PlaceOrderCommand_ShouldRoundTripThroughAbstractBaseIntact()
    {
        var sent = new PlaceOrderCommand(FixedAggregateId, "ITEM-1")
        {
            CommandId = PlaceCommandId,
        };

        // Parameter is the abstract Command — proves polymorphism with no [Union].
        EdictCommand asBase = sent;
        var result = await fixture.Sender.Send(asBase);

        await VerifyRoundTrip(result, CommandRoundTripRecorder.Get(sent.CommandId));
    }

    [Fact]
    public async Task CancelOrderCommand_ShouldRoundTripThroughAbstractBaseIntact()
    {
        var sent = new CancelOrderCommand(FixedAggregateId, "customer-request")
        {
            CommandId = CancelCommandId,
        };

        EdictCommand asBase = sent;
        var result = await fixture.Sender.Send(asBase);

        await VerifyRoundTrip(result, CommandRoundTripRecorder.Get(sent.CommandId));
    }

    [Fact]
    public async Task MixedPrimitiveCommand_ShouldRoundTripEveryPrimitiveIntact()
    {
        var sent = new MixedPrimitiveCommand(FixedAggregateId)
        {
            CommandId = MixedCommandId,
            Label = "probe-label",
            Count = 42,
            Flag = true,
            Ratio = 3.5,
            OccurredAt = new DateTimeOffset(2026, 5, 17, 9, 30, 0, TimeSpan.FromHours(1)),
            Note = "nullable-present",
        };

        EdictCommand asBase = sent;
        var result = await fixture.Sender.Send(asBase);

        await VerifyRoundTrip(result, CommandRoundTripRecorder.Get(sent.CommandId));
    }

    [Fact]
    public async Task FailOrderCommand_ShouldRoundTripToHandlerThenFault()
    {
        var sent = new FailOrderCommand(FixedAggregateId)
        {
            CommandId = FailCommandId,
        };

        EdictCommand asBase = sent;

        // The handler's own exception — not a CodecNotFoundException — proves
        // the body deserialized on the silo and reached Handle. The snapshot
        // pins the exception type and message.
        var thrown = await Record.ExceptionAsync(() => fixture.Sender.Send(asBase));

        await Verify(new { thrown!.GetType().Name, thrown.Message });
    }

    // One snapshot capturing the outcome envelope, the concrete runtime type
    // that survived the hop (the no-[Union] polymorphism proof), and every
    // round-tripped property value. Guids and dates are left unscrubbed: the
    // inputs are fixed constants, so the literal routing id, command id and
    // timestamp are themselves the round-trip assertion.
    private static SettingsTask VerifyRoundTrip(EdictCommandResult result, EdictCommand received) =>
        Verify(new
        {
            Result = result,
            ReceivedType = received.GetType().FullName,
            Received = received,
        }).DontScrubGuids().DontScrubDateTimes();
}
