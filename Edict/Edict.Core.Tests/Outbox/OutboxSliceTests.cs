using Edict.Core.Outbox;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Outbox;

// Pure state-machine semantics of the Outbox/DeadLetter slice (ADR 0018 / 0019).
// In-memory, no backend (ADR 0016). Every input is a fixed constant so the
// Verify snapshot is deterministic and the literal values are the assertion;
// Guids/dates are left unscrubbed for the same reason.

public sealed class OutboxSliceTests
{
    private static readonly Guid EntryA = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid EntryB = new("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    private static OutboxEntry Entry(Guid id, OutboxEffectKind kind) => new()
    {
        EntryId = id,
        Kind = kind,
        Payload = [1, 2, 3],
        TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
        TraceState = null,
    };

    [Fact]
    public Task Enqueue_ShouldAppendToPendingTail()
    {
        var slice = new OutboxSlice()
            .Enqueue(Entry(EntryA, OutboxEffectKind.PublishEvent));

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task AckHead_ShouldRemovePendingHead_PreservingFifoOrder()
    {
        var slice = new OutboxSlice()
            .Enqueue(Entry(EntryA, OutboxEffectKind.PublishEvent))
            .Enqueue(Entry(EntryB, OutboxEffectKind.SendCommand))
            .AckHead();

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task AckHead_ShouldBeNoOp_WhenPendingEmpty()
    {
        var slice = new OutboxSlice().AckHead();

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task FailHeadWithBackoff_ShouldBumpAttemptAndGateNextAttempt()
    {
        var baseDelay = TimeSpan.FromSeconds(2);

        var slice = new OutboxSlice()
            .Enqueue(Entry(EntryA, OutboxEffectKind.PublishEvent))
            .Enqueue(Entry(EntryB, OutboxEffectKind.SendCommand))
            .FailHeadWithBackoff(Now, baseDelay)
            .FailHeadWithBackoff(Now, baseDelay);

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task FailHeadWithBackoff_ShouldBeNoOp_WhenPendingEmpty()
    {
        var slice = new OutboxSlice().FailHeadWithBackoff(Now, TimeSpan.FromSeconds(2));

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task DeadLetterHead_ShouldMovePendingHeadIntoDeadLetterSlice()
    {
        var slice = new OutboxSlice()
            .Enqueue(Entry(EntryA, OutboxEffectKind.PublishEvent))
            .Enqueue(Entry(EntryB, OutboxEffectKind.SendCommand))
            .FailHeadWithBackoff(Now, TimeSpan.FromSeconds(2))
            .DeadLetterHead(Now, "max attempts exhausted");

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task DeadLetterHead_ShouldBeNoOp_WhenPendingEmpty()
    {
        var slice = new OutboxSlice().DeadLetterHead(Now, "unreachable");

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task Redrive_ShouldMoveDeadLetterEntryBackToPendingTail_WithAttemptReset()
    {
        var slice = new OutboxSlice()
            .Enqueue(Entry(EntryA, OutboxEffectKind.PublishEvent))
            .Enqueue(Entry(EntryB, OutboxEffectKind.SendCommand))
            .FailHeadWithBackoff(Now, TimeSpan.FromSeconds(2))
            .DeadLetterHead(Now, "downstream outage")
            .Redrive(EntryA, Now.AddHours(1));

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task Redrive_ShouldBeNoOp_WhenEntryNotInDeadLetterSlice()
    {
        var slice = new OutboxSlice()
            .Enqueue(Entry(EntryA, OutboxEffectKind.PublishEvent))
            .Redrive(EntryB, Now);

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }
}
