using Edict.Core.Idempotency;

namespace Edict.Core.Tests.Idempotency;

public sealed class DedupRingMirrorTests
{
    static readonly Guid EventA = new("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid EventB = new("bbbbbbbb-0000-0000-0000-000000000002");
    static readonly Guid EventC = new("cccccccc-0000-0000-0000-000000000003");
    static readonly Guid EventD = new("dddddddd-0000-0000-0000-000000000004");
    static readonly Guid EventE = new("eeeeeeee-0000-0000-0000-000000000005");
    static readonly Guid EventF = new("ffffffff-0000-0000-0000-000000000006");

    [Fact]
    public void Activate_FromPartiallyFilledRing_ContainsOnlyPopulatedSlots()
    {
        var ring = new Guid[5];
        ring[0] = EventA;
        ring[1] = EventB;

        var mirror = new DedupRingMirror();
        mirror.Activate(ring, head: 2, count: 2);

        Assert.True(mirror.Contains(EventA));
        Assert.True(mirror.Contains(EventB));
        Assert.False(mirror.Contains(EventC));
        Assert.False(mirror.Contains(Guid.Empty));
    }

    [Fact]
    public void Commit_PastWindowSize_EvictsDisplacedId()
    {
        var mirror = new DedupRingMirror();
        mirror.Activate(new Guid[3], head: 0, count: 0);

        mirror.Commit(EventA);
        mirror.Commit(EventB);
        mirror.Commit(EventC);
        mirror.Commit(EventD);

        Assert.False(mirror.Contains(EventA));
        Assert.True(mirror.Contains(EventB));
        Assert.True(mirror.Contains(EventC));
        Assert.True(mirror.Contains(EventD));
    }

    [Fact]
    public void Contains_MatchesPersistedRingSlowPathScan_AcrossRotation()
    {
        const int windowSize = 4;
        var persistedRing = new Guid[windowSize];
        var persistedHead = 0;
        var persistedCount = 0;

        var mirror = new DedupRingMirror();
        mirror.Activate(persistedRing, persistedHead, persistedCount);

        var commits = new[] { EventA, EventB, EventC, EventD, EventE, EventF };
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

        var probes = new[] { EventA, EventB, EventC, EventD, EventE, EventF, Guid.Empty, Guid.NewGuid() };
        foreach (var id in probes)
        {
            var slowPath = persistedCount < windowSize
                ? Array.IndexOf(persistedRing, id, 0, persistedCount) >= 0
                : Array.IndexOf(persistedRing, id) >= 0;
            Assert.Equal(slowPath, mirror.Contains(id));
        }
    }
}
