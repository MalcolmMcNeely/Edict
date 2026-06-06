using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Projections;

using Orleans;

namespace Edict.Core.Tests.Projections;

// White-box interleave workload for the in-grain (State) projection dedup path. A
// delivery parks inside the handler on a static gate so a concurrent same-EventId
// redelivery can race the dedup claim while the first turn is mid-flight; the test
// then releases the gate and asserts the handler ran exactly once. The gate is
// static so the in-grain handler (which the consumer never constructs) can reach
// it; the owning test class is serialized and resets the gate per test, so it
// never cross-talks.

[GenerateSerializer]
[Alias("Edict.Core.Tests.Projections.GatedProjection")]
public sealed class GatedProjection : IEdictPersistedState
{
    [Id(0)]
    public int Total { get; set; }
}

[EdictStream("GatedProjectionInterleave")]
public sealed partial record GatedProjectionEvent(Guid AggregateId) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;
}

static class GatedProjectionGate
{
    public static int EnteredCount;
    public static TaskCompletionSource FirstEntered = Fresh();
    public static TaskCompletionSource SecondEntered = Fresh();
    public static TaskCompletionSource Release = Fresh();

    static TaskCompletionSource Fresh() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static void Reset()
    {
        EnteredCount = 0;
        FirstEntered = Fresh();
        SecondEntered = Fresh();
        Release = Fresh();
    }
}

public sealed partial class GatedStateProjectionBuilder : EdictProjectionBuilder<GatedProjection>
{
    async Task HandleAsync(GatedProjectionEvent edictEvent)
    {
        Projection.Total++;
        var ordinal = Interlocked.Increment(ref GatedProjectionGate.EnteredCount);
        if (ordinal == 1)
        {
            GatedProjectionGate.FirstEntered.TrySetResult();
        }
        else
        {
            GatedProjectionGate.SecondEntered.TrySetResult();
        }
        await GatedProjectionGate.Release.Task;
    }
}
