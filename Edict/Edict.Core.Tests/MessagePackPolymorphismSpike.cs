using System.Collections.Concurrent;

using Edict.Abstractions;
using Edict.Core;

using MessagePack;

namespace Edict.Core.Tests;

// ADR 0007 go/no-go spike. Proves a concrete consumer command, sent through
// IEdictSender.Send(Command) typed as the abstract base with NO [Union] on the
// base or subtype, round-trips across a real Orleans grain hop on the
// MessagePack serializer with its concrete runtime type and every property
// value intact. If this does not hold, ADR 0007 must be re-decided by a human
// before the rename/pipeline slices proceed.

[MessagePackObject(keyAsPropertyName: true)]
public sealed record SpikeProbeCommand(Guid ProbeId) : Command
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

// Single-process in-memory TestCluster: a static sink is the simplest way for
// the receiving grain to surface what it actually deserialized so the test can
// assert on the concrete type and values from the sender side.
public static class SpikeRecorder
{
    private static readonly ConcurrentDictionary<Guid, Command> Received = new();

    public static void Record(Command command) => Received[Key(command)] = command;

    public static Command Get(Guid probeId) => Received[probeId];

    private static Guid Key(Command command) => ((SpikeProbeCommand)command).ProbeId;
}

public partial class SpikeProbeGrain : CommandHandlerGrain
{
    public Task<CommandResult> Handle(SpikeProbeCommand command)
    {
        SpikeRecorder.Record(command);
        return Task.FromResult<CommandResult>(new CommandResult.Accepted());
    }
}

public sealed class MessagePackPolymorphismSpike(EdictClusterFixture fixture)
    : IClassFixture<EdictClusterFixture>
{
    [Fact]
    public async Task Derived_command_sent_through_abstract_base_round_trips_intact_on_MessagePack()
    {
        var sent = new SpikeProbeCommand(Guid.NewGuid())
        {
            Label = "probe-label",
            Count = 42,
            Flag = true,
            Ratio = 3.5,
            OccurredAt = new DateTimeOffset(2026, 5, 17, 9, 30, 0, TimeSpan.FromHours(1)),
            Note = "nullable-present",
        };

        // Parameter is the abstract Command — proves polymorphism with no [Union].
        Command asBase = sent;
        var result = await fixture.Sender.Send(asBase);

        Assert.IsType<CommandResult.Accepted>(result);

        var received = SpikeRecorder.Get(sent.ProbeId);

        // Concrete runtime type preserved across the hop.
        var typed = Assert.IsType<SpikeProbeCommand>(received);

        // Every property value preserved.
        Assert.Equal(sent.ProbeId, typed.ProbeId);
        Assert.Equal(sent.CommandId, typed.CommandId);
        Assert.Equal(sent.Label, typed.Label);
        Assert.Equal(sent.Count, typed.Count);
        Assert.Equal(sent.Flag, typed.Flag);
        Assert.Equal(sent.Ratio, typed.Ratio);
        Assert.Equal(sent.OccurredAt, typed.OccurredAt);
        Assert.Equal(sent.Note, typed.Note);
    }
}
