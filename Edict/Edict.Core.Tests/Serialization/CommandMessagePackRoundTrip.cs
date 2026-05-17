using System.Collections.Concurrent;

using Edict.Contracts.Commands;
using Edict.Contracts.Results;
using Edict.Contracts.Sending;
using Edict.Core.Grains;

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
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MixedPrimitiveCommand(Guid ProbeId) : Command
{
    [RouteKey]
    public Guid ProbeId { get; init; } = ProbeId;

    public string Label { get; init; } = "";

    public int Count { get; init; }

    public bool Flag { get; init; }

    public double Ratio { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public string? Note { get; init; }
}

public partial class MixedPrimitiveGrain : Edict.Core.Grains.CommandHandlerGrain
{
    public Task<CommandResult> Handle(MixedPrimitiveCommand command)
    {
        CommandRoundTripRecorder.Record(command);
        return Task.FromResult<CommandResult>(new CommandResult.Accepted());
    }
}

// Single-process in-memory TestCluster: a static sink keyed by CommandId is
// the simplest way for the receiving grain to surface what it actually
// deserialized so the test can snapshot it from the sender side. Works for
// any Command — every Command carries a CommandId.
public static class CommandRoundTripRecorder
{
    private static readonly ConcurrentDictionary<Guid, Command> Received = new();

    public static void Record(Command command) => Received[command.CommandId] = command;

    public static Command Get(Guid commandId) => Received[commandId];
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
    public async Task PlaceOrderCommand_round_trips_through_abstract_base_intact()
    {
        var sent = new PlaceOrderCommand(FixedAggregateId, "ITEM-1")
        {
            CommandId = PlaceCommandId,
        };

        // Parameter is the abstract Command — proves polymorphism with no [Union].
        Command asBase = sent;
        var result = await fixture.Sender.Send(asBase);

        await VerifyRoundTrip(result, CommandRoundTripRecorder.Get(sent.CommandId));
    }

    [Fact]
    public async Task CancelOrderCommand_round_trips_through_abstract_base_intact()
    {
        var sent = new CancelOrderCommand(FixedAggregateId, "customer-request")
        {
            CommandId = CancelCommandId,
        };

        Command asBase = sent;
        var result = await fixture.Sender.Send(asBase);

        await VerifyRoundTrip(result, CommandRoundTripRecorder.Get(sent.CommandId));
    }

    [Fact]
    public async Task MixedPrimitiveCommand_round_trips_every_primitive_intact()
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

        Command asBase = sent;
        var result = await fixture.Sender.Send(asBase);

        await VerifyRoundTrip(result, CommandRoundTripRecorder.Get(sent.CommandId));
    }

    [Fact]
    public async Task FailOrderCommand_round_trips_to_the_handler_then_faults()
    {
        var sent = new FailOrderCommand(FixedAggregateId)
        {
            CommandId = FailCommandId,
        };

        Command asBase = sent;

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
    private static SettingsTask VerifyRoundTrip(CommandResult result, Command received) =>
        Verify(new
        {
            Result = result,
            ReceivedType = received.GetType().FullName,
            Received = received,
        }).DontScrubGuids().DontScrubDateTimes();
}
