using Edict.Core.Idempotency;
using Edict.Core.Tests.TestSupport;

using Xunit;

namespace Edict.Core.Tests.Projections;

// Deterministic white-box proof that the in-grain projection dedup path closes the
// concurrent-redelivery window: a delivery parked inside the handler holds the
// in-flight claim, so a same-EventId redelivery that interleaves under
// [AlwaysInterleave] is suppressed at the claim and the handler runs exactly once.
// Red before the four-path fix (the bare Contains gate let both deliveries through).
[Collection(SubstrateIndependentCollection.Name)]
public sealed class ProjectionDedupInterleaveTests(SubstrateIndependentClusterFixture fixture)
{
    [Fact]
    public async Task ConcurrentRedelivery_OfSameEventId_RunsHandlerExactlyOnce()
    {
        // Arrange
        GatedProjectionGate.Reset();
        var aggregateId = Guid.NewGuid();
        var consumer = fixture.GrainFactory.GetGrain<IEdictEventConsumer>(
            aggregateId, grainClassNamePrefix: typeof(GatedStateProjectionBuilder).FullName);
        var edictEvent = new GatedProjectionEvent(aggregateId) { EventId = Guid.NewGuid() };

        // Act — delivery A parks inside the handler; delivery B redelivers the same
        // EventId while A is mid-flight. The release is held until B has either
        // entered the handler (unfixed: it raced past the dedup gate) or completed
        // (fixed: it was suppressed at the claim), so the interleave is deterministic
        // with no timing window. Then the gate releases and both turns drain.
        var first = consumer.OnEdictEventAsync(edictEvent);
        await GatedProjectionGate.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = consumer.OnEdictEventAsync(edictEvent);
        await Task.WhenAny(GatedProjectionGate.SecondEntered.Task, second).WaitAsync(TimeSpan.FromSeconds(10));
        GatedProjectionGate.Release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        Assert.Equal(1, GatedProjectionGate.EnteredCount);
    }
}
