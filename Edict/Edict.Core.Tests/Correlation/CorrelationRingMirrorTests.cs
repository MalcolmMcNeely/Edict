using Edict.Core.Correlation;

namespace Edict.Core.Tests.Correlation;

public sealed class CorrelationRingMirrorTests
{
    static readonly Guid CorrelationA = new("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid CorrelationB = new("bbbbbbbb-0000-0000-0000-000000000002");
    static readonly Guid CorrelationC = new("cccccccc-0000-0000-0000-000000000003");
    static readonly Guid CorrelationD = new("dddddddd-0000-0000-0000-000000000004");
    static readonly Guid CorrelationE = new("eeeeeeee-0000-0000-0000-000000000005");
    static readonly Guid CorrelationF = new("ffffffff-0000-0000-0000-000000000006");

    [Fact]
    public void Activate_FromPartiallyFilledRing_ContainsOnlyPopulatedSlots()
    {
        var ring = new Guid[5];
        ring[0] = CorrelationA;
        ring[1] = CorrelationB;

        var mirror = new CorrelationRingMirror();
        mirror.Activate(ring, head: 2, count: 2);

        Assert.True(mirror.Contains(CorrelationA));
        Assert.True(mirror.Contains(CorrelationB));
        Assert.False(mirror.Contains(CorrelationC));
        Assert.False(mirror.Contains(Guid.Empty));
    }

    [Fact]
    public void Commit_PastWindowSize_EvictsDisplacedId()
    {
        var mirror = new CorrelationRingMirror();
        mirror.Activate(new Guid[3], head: 0, count: 0);

        mirror.Commit(CorrelationA);
        mirror.Commit(CorrelationB);
        mirror.Commit(CorrelationC);
        mirror.Commit(CorrelationD);

        Assert.False(mirror.Contains(CorrelationA));
        Assert.True(mirror.Contains(CorrelationB));
        Assert.True(mirror.Contains(CorrelationC));
        Assert.True(mirror.Contains(CorrelationD));
    }

    [Fact]
    public void Contains_MatchesPersistedRingSlowPathScan_AcrossRotation()
    {
        const int windowSize = 4;
        var persistedRing = new Guid[windowSize];
        var persistedHead = 0;
        var persistedCount = 0;

        var mirror = new CorrelationRingMirror();
        mirror.Activate(persistedRing, persistedHead, persistedCount);

        var commits = new[] { CorrelationA, CorrelationB, CorrelationC, CorrelationD, CorrelationE, CorrelationF };
        foreach (var id in commits)
        {
            persistedRing[persistedHead] = id;
            persistedHead = (persistedHead + 1) % windowSize;
            if (persistedCount < windowSize)
            {
                persistedCount++;
            }

            mirror.Commit(id);
        }

        var probes = new[] { CorrelationA, CorrelationB, CorrelationC, CorrelationD, CorrelationE, CorrelationF, Guid.Empty, Guid.NewGuid() };
        foreach (var id in probes)
        {
            var slowPath = persistedCount < windowSize
                ? Array.IndexOf(persistedRing, id, 0, persistedCount) >= 0
                : Array.IndexOf(persistedRing, id) >= 0;
            Assert.Equal(slowPath, mirror.Contains(id));
        }
    }
}
