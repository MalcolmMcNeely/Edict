using Edict.Core.Outbox;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Outbox;

public sealed class OutboxBatchGroupingTests
{
    static readonly Guid EntryA = new("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid EntryB = new("bbbbbbbb-0000-0000-0000-000000000002");
    static readonly Guid EntryC = new("cccccccc-0000-0000-0000-000000000003");
    static readonly Guid EntryD = new("dddddddd-0000-0000-0000-000000000004");
    static readonly Guid EntryE = new("eeeeeeee-0000-0000-0000-000000000005");

    static readonly Guid RouteKeyX = new("11111111-1111-1111-1111-111111111111");
    static readonly Guid RouteKeyY = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public Task Group_SingleEntry_ShouldProduceOneOneElementGroup()
    {
        OutboxEntry[] entries =
        [
            Entry(EntryA, "orders", RouteKeyX),
        ];

        var groups = OutboxBatchGrouping.Group(entries, KeyFor);

        return Verify(groups).DontScrubGuids();
    }

    [Fact]
    public Task Group_TwoEntriesSameKey_ShouldProduceOneTwoElementGroup()
    {
        OutboxEntry[] entries =
        [
            Entry(EntryA, "orders", RouteKeyX),
            Entry(EntryB, "orders", RouteKeyX),
        ];

        var groups = OutboxBatchGrouping.Group(entries, KeyFor);

        return Verify(groups).DontScrubGuids();
    }

    [Fact]
    public Task Group_InterleavedKeys_ShouldNotMergeNonContiguousMatches()
    {
        // Same stream X, then Y, then X again — the trailing X must NOT merge
        // with the leading X. Insertion order is load-bearing for at-most-out-
        // of-order delivery: consecutive-only is the only safe grouping.
        OutboxEntry[] entries =
        [
            Entry(EntryA, "orders", RouteKeyX),
            Entry(EntryB, "orders", RouteKeyY),
            Entry(EntryC, "orders", RouteKeyX),
        ];

        var groups = OutboxBatchGrouping.Group(entries, KeyFor);

        return Verify(groups).DontScrubGuids();
    }

    [Fact]
    public Task Group_MixedRun_ShouldPreserveInsertionOrderWithinAndAcrossGroups()
    {
        // Two X then two Y then one X: 3 groups expected, insertion-order
        // preserved within each group and across groups.
        OutboxEntry[] entries =
        [
            Entry(EntryA, "orders", RouteKeyX),
            Entry(EntryB, "orders", RouteKeyX),
            Entry(EntryC, "orders", RouteKeyY),
            Entry(EntryD, "orders", RouteKeyY),
            Entry(EntryE, "orders", RouteKeyX),
        ];

        var groups = OutboxBatchGrouping.Group(entries, KeyFor);

        return Verify(groups).DontScrubGuids();
    }

    static OutboxEntry Entry(Guid id, string streamName, Guid routeKey) => new()
    {
        EntryId = id,
        Kind = OutboxEffectKind.PublishEvent,
        Payload = [(byte)id.GetHashCode()],
        // Stash the stream key in TraceState for the test key selector — keeps
        // OutboxEntry itself stream-agnostic, which is correct for the slice's
        // public shape.
        TraceState = $"{streamName}|{routeKey:D}",
    };

    static (string StreamName, Guid RouteKey) KeyFor(OutboxEntry entry)
    {
        var parts = entry.TraceState!.Split('|');
        return (parts[0], Guid.Parse(parts[1]));
    }
}
