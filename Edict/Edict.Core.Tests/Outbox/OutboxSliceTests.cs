using Edict.Contracts.Configuration;
using Edict.Core.Outbox;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Outbox;

// Pure state-machine semantics of the Outbox slice (ADR 0018 / 0022 / 0026).
// In-memory, no backend (ADR 0016). Every input is a fixed constant so the
// Verify snapshot is deterministic and the literal values are the assertion;
// Guids/dates are left unscrubbed for the same reason.

public sealed class OutboxSliceTests
{
    private static readonly Guid EntryA = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid EntryB = new("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid EntryC = new("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid PromotedId = new("dddddddd-0000-0000-0000-000000000099");
    private static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly EdictOutboxOptions Options = new();

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
        // ADR 0026: failing entry stays in place; insertion order is preserved
        // and the drain walks past it (no head privilege). A second failure
        // bumps AttemptCount again.
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
