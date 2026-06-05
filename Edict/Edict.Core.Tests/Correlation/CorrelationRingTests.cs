using Edict.Core.Correlation;

namespace Edict.Core.Tests.Correlation;

public sealed class CorrelationRingTests
{
    static readonly Guid CorrelationA = new("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid CorrelationB = new("bbbbbbbb-0000-0000-0000-000000000002");
    static readonly Guid CorrelationC = new("cccccccc-0000-0000-0000-000000000003");

    [Fact]
    public void Apply_AdvancesHeadAndCountAndWritesSlot()
    {
        // Arrange
        var ring = new CorrelationProgress { CorrelationIds = new Guid[3], Head = 0, Count = 0 };

        // Act
        CorrelationRing.Apply(ring, windowSize: 3, CorrelationA);

        // Assert
        Assert.Equal(CorrelationA, ring.CorrelationIds[0]);
        Assert.Equal(1, ring.Head);
        Assert.Equal(1, ring.Count);
    }

    [Fact]
    public void RollBack_AfterApply_RestoresRingExactly_WhenOverwritingAFilledSlot()
    {
        // Arrange — a full ring whose head slot already holds a correlation the
        // apply would displace; the rollback must put that displaced id back.
        var ring = new CorrelationProgress
        {
            CorrelationIds = [CorrelationA, CorrelationB, CorrelationC],
            Head = 0,
            Count = 3,
        };
        var beforeHead = ring.Head;
        var beforeCount = ring.Count;
        var beforeSlots = (Guid[])ring.CorrelationIds.Clone();

        // Act
        var revert = CorrelationRing.Apply(ring, windowSize: 3, Guid.NewGuid());
        CorrelationRing.RollBack(ring, revert);

        // Assert
        Assert.Equal(beforeHead, ring.Head);
        Assert.Equal(beforeCount, ring.Count);
        Assert.Equal(beforeSlots, ring.CorrelationIds);
    }
}
