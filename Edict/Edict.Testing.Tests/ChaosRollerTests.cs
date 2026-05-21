using Edict.Core.EventHandler;
using Edict.Testing.Internal;

using Xunit;

namespace Edict.Testing.Tests;

public sealed class ChaosRollerTests
{
    sealed class FakeProjection { }

    abstract class FakeHandler : EdictEventHandler { }

    static ChaosOptions Options(
        int seed = 0xED1C7,
        double duplicate = 0.5,
        int maxExtras = 1,
        double reorder = 0.3,
        int maxDistance = 2,
        bool invocationsEnabled = false) =>
        new(
            Seed: seed,
            DuplicateProbability: duplicate,
            MaxExtraDeliveries: maxExtras,
            ReorderProbability: reorder,
            MaxReorderDistance: maxDistance,
            InvocationsEnabled: invocationsEnabled);

    [Fact]
    public void ShouldHold_IsDeterministic_AcrossInstancesWithSameSeed()
    {
        var a = new ChaosRoller(Options());
        var b = new ChaosRoller(Options());

        var sequenceA = Enumerable.Range(0, 32)
            .Select(_ => a.ShouldHold(typeof(FakeProjection)))
            .ToArray();
        var sequenceB = Enumerable.Range(0, 32)
            .Select(_ => b.ShouldHold(typeof(FakeProjection)))
            .ToArray();

        Assert.Equal(sequenceA, sequenceB);
    }

    [Fact]
    public void ShouldHold_IsIndependentOf_DuplicateProbability()
    {
        // The two-RNG design lets each knob move without re-baselining tests
        // gated by the other. Pinning DuplicateProbability to two extremes must
        // not perturb the reorder stream.
        var a = new ChaosRoller(Options(duplicate: 0.0));
        var b = new ChaosRoller(Options(duplicate: 1.0));

        var sequenceA = Enumerable.Range(0, 32)
            .Select(_ => a.ShouldHold(typeof(FakeProjection)))
            .ToArray();
        var sequenceB = Enumerable.Range(0, 32)
            .Select(_ => b.ShouldHold(typeof(FakeProjection)))
            .ToArray();

        Assert.Equal(sequenceA, sequenceB);
    }

    [Fact]
    public void ExtraDeliveries_IsIndependentOf_ReorderProbability()
    {
        var a = new ChaosRoller(Options(reorder: 0.0));
        var b = new ChaosRoller(Options(reorder: 1.0));

        var sequenceA = Enumerable.Range(0, 32)
            .Select(_ => a.ExtraDeliveries(typeof(FakeProjection)))
            .ToArray();
        var sequenceB = Enumerable.Range(0, 32)
            .Select(_ => b.ExtraDeliveries(typeof(FakeProjection)))
            .ToArray();

        Assert.Equal(sequenceA, sequenceB);
    }

    [Fact]
    public void ShouldHold_HoldDistance_StaysWithinConfiguredRange()
    {
        // Reorder distance is sampled as [1, MaxReorderDistance] inclusive.
        var roller = new ChaosRoller(Options(reorder: 1.0, maxDistance: 4));

        foreach (var _ in Enumerable.Range(0, 64))
        {
            var (hold, distance) = roller.ShouldHold(typeof(FakeProjection));
            Assert.True(hold);
            Assert.InRange(distance, 1, 4);
        }
    }

    [Fact]
    public void ExtraDeliveries_ReturnsZero_ForEventHandlers_WhenInvocationsDisabled()
    {
        // The carve-out keeps mock call-count assertions on EdictEventHandler
        // deterministic — duplicates would break Times.Once.
        var roller = new ChaosRoller(Options(duplicate: 1.0, maxExtras: 5));

        foreach (var _ in Enumerable.Range(0, 16))
        {
            Assert.Equal(0, roller.ExtraDeliveries(typeof(FakeHandler)));
        }
    }

    [Fact]
    public void ExtraDeliveries_IncludesEventHandlers_WhenInvocationsEnabled()
    {
        var roller = new ChaosRoller(Options(duplicate: 1.0, maxExtras: 1, invocationsEnabled: true));

        // With duplicate p=1 and InvocationsEnabled=true the handler grain
        // gets at least one extra delivery on every roll.
        foreach (var _ in Enumerable.Range(0, 16))
        {
            Assert.Equal(1, roller.ExtraDeliveries(typeof(FakeHandler)));
        }
    }
}
