using Edict.Core.Projections;

using Orleans.Concurrency;

namespace Edict.Core.Tests.Projections;

// A projection read parks an in-grain wait (slice 2) on a single-activation,
// turn-based grain. Without interleaving, the stream-delivery turn that would
// satisfy the wait queues behind the read and the activation self-deadlocks.
// The attribute is therefore load-bearing and lives on the hand-written grain
// interface the client proxy dispatches through — Orleans honours it there, not
// on the implementation. A forgotten attribute is a silent hang, so it is
// pinned by reflection.
public class ProjectionReadInterleaveTests
{
    [Theory]
    [InlineData(nameof(IEdictProjectionBuilder.EdictReadRowAsync))]
    [InlineData(nameof(IEdictProjectionBuilder.EdictReadPartitionAsync))]
    public void ProjectionReadMethod_ShouldBeMarkedAlwaysInterleave(string methodName)
    {
        var method = typeof(IEdictProjectionBuilder).GetMethod(methodName)
            ?? throw new InvalidOperationException($"{methodName} is missing from IEdictProjectionBuilder.");

        var hasAlwaysInterleave = Attribute.IsDefined(method, typeof(AlwaysInterleaveAttribute));

        Assert.True(hasAlwaysInterleave, $"{methodName} must carry [AlwaysInterleave].");
    }
}
