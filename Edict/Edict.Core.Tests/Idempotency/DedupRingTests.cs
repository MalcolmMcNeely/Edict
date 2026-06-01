using Edict.Core.Idempotency;

namespace Edict.Core.Tests.Idempotency;

public sealed class DedupRingTests
{
    static readonly Guid EventA = new("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid EventB = new("bbbbbbbb-0000-0000-0000-000000000002");
    static readonly Guid EventC = new("cccccccc-0000-0000-0000-000000000003");

    [Fact]
    public void Apply_AdvancesHeadAndCountAndWritesSlot()
    {
        // Arrange
        var ring = new IdempotencyState { HandledEventIds = new Guid[3], Head = 0, Count = 0 };

        // Act
        DedupRing.Apply(ring, windowSize: 3, EventA);

        // Assert
        Assert.Equal(EventA, ring.HandledEventIds[0]);
        Assert.Equal(1, ring.Head);
        Assert.Equal(1, ring.Count);
    }

    [Fact]
    public void RollBack_AfterApply_RestoresRingExactly_WhenOverwritingAFilledSlot()
    {
        // Arrange — a full ring whose head slot already holds an id the apply
        // would displace; the rollback must put that displaced id back.
        var ring = new IdempotencyState
        {
            HandledEventIds = [EventA, EventB, EventC],
            Head = 0,
            Count = 3,
        };
        var beforeHead = ring.Head;
        var beforeCount = ring.Count;
        var beforeSlots = (Guid[])ring.HandledEventIds.Clone();

        // Act
        var revert = DedupRing.Apply(ring, windowSize: 3, Guid.NewGuid());
        DedupRing.RollBack(ring, revert);

        // Assert
        Assert.Equal(beforeHead, ring.Head);
        Assert.Equal(beforeCount, ring.Count);
        Assert.Equal(beforeSlots, ring.HandledEventIds);
    }
}
