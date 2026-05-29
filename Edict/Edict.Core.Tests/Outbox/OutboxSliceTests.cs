using System.Collections.Immutable;

using Edict.Contracts.Configuration;
using Edict.Core.Outbox;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Outbox;

public sealed class OutboxSliceTests
{
    static readonly Guid EntryA = new("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid EntryB = new("bbbbbbbb-0000-0000-0000-000000000002");
    static readonly Guid EntryC = new("cccccccc-0000-0000-0000-000000000003");
    static readonly Guid PromotedId = new("dddddddd-0000-0000-0000-000000000099");
    static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);
    static readonly EdictOptions Options = new();

    static OutboxEntry Entry(Guid id, OutboxEffectKind kind) => new()
    {
        EntryId = id,
        Kind = kind,
        Payload = [1, 2, 3],
        TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
        TraceState = null,
    };

    [Fact]
    public void Pending_ShouldBe_ImmutableList_ForStructuralSharing()
    {
        var slice = new OutboxSlice()
            .Enqueue(Entry(EntryA, OutboxEffectKind.PublishEvent));

        Assert.IsType<ImmutableList<OutboxEntry>>(slice.Pending);
    }

    [Fact]
    public Task Enqueue_ShouldAppendToPendingTail()
    {
        var slice = new OutboxSlice()
            .Enqueue(Entry(EntryA, OutboxEffectKind.PublishEvent));

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task Ack_ShouldRemoveMatchingEntry_PreservingInsertionOrder()
    {
        var slice = new OutboxSlice()
            .Enqueue(Entry(EntryA, OutboxEffectKind.PublishEvent))
            .Enqueue(Entry(EntryB, OutboxEffectKind.SendCommand))
            .Enqueue(Entry(EntryC, OutboxEffectKind.UpsertRow))
            .Ack(EntryB);

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task Ack_ShouldBeNoOp_WhenEntryIdNotPresent()
    {
        var slice = new OutboxSlice()
            .Enqueue(Entry(EntryA, OutboxEffectKind.PublishEvent))
            .Ack(EntryB);

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task Ack_ShouldBeNoOp_WhenPendingEmpty()
    {
        var slice = new OutboxSlice().Ack(EntryA);

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task FailWithBackoff_ShouldBumpAttemptAndGateNextAttempt_OfMatchingEntry()
    {
        var slice = new OutboxSlice()
            .Enqueue(Entry(EntryA, OutboxEffectKind.PublishEvent))
            .Enqueue(Entry(EntryB, OutboxEffectKind.SendCommand))
            .FailWithBackoff(EntryB, Now, Options)
            .FailWithBackoff(EntryB, Now, Options);

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task FailWithBackoff_ShouldBeNoOp_WhenEntryIdNotPresent()
    {
        var slice = new OutboxSlice()
            .Enqueue(Entry(EntryA, OutboxEffectKind.PublishEvent))
            .FailWithBackoff(EntryB, Now, Options);

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task FailWithBackoff_ShouldBeNoOp_WhenPendingEmpty()
    {
        var slice = new OutboxSlice().FailWithBackoff(EntryA, Now, Options);

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task Promote_ShouldRemoveFailingEntryAndAppendPromotedAtTail()
    {
        var promoted = new OutboxEntry
        {
            EntryId = PromotedId,
            Kind = OutboxEffectKind.PublishEvent,
            Payload = [9, 9, 9],
            NextAttemptUtc = Now,
        };

        var slice = new OutboxSlice()
            .Enqueue(Entry(EntryA, OutboxEffectKind.PublishEvent))
            .Enqueue(Entry(EntryB, OutboxEffectKind.SendCommand))
            .Enqueue(Entry(EntryC, OutboxEffectKind.UpsertRow))
            .FailWithBackoff(EntryB, Now, Options)
            .Promote(EntryB, promoted);

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task Promote_ShouldBeNoOp_WhenEntryIdNotPresent()
    {
        var promoted = new OutboxEntry
        {
            EntryId = PromotedId,
            Kind = OutboxEffectKind.PublishEvent,
            NextAttemptUtc = Now,
        };

        var slice = new OutboxSlice()
            .Enqueue(Entry(EntryA, OutboxEffectKind.PublishEvent))
            .Promote(EntryB, promoted);

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task Promote_ShouldBeNoOp_WhenPendingEmpty()
    {
        var promoted = new OutboxEntry
        {
            EntryId = PromotedId,
            Kind = OutboxEffectKind.PublishEvent,
            NextAttemptUtc = Now,
        };

        var slice = new OutboxSlice().Promote(EntryA, promoted);

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }
}
