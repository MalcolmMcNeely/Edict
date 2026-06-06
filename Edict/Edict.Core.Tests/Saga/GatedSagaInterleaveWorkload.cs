using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core.Sagas;

using Orleans;

namespace Edict.Core.Tests.Saga;

// White-box interleave workload for the saga dedup path. The handler parks on a
// static gate so a concurrent same-EventId redelivery can race the dedup claim
// while the first turn is mid-handler (between the dedup check and the post-handler
// commit); the test releases the gate and asserts the handler ran, and dispatched
// its outbound command, exactly once. Static so the in-grain handler can reach it;
// the owning test class is serialized and resets the gate per test.

[EdictStream("SagaDedupInterleave")]
public sealed partial record GatedSagaEvent(Guid WorkflowId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

static class GatedSagaGate
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

public partial class GatedSaga : EdictSaga<LifecycleProgress>
{
    async Task HandleAsync(GatedSagaEvent edictEvent)
    {
        Progress.Handled++;
        var ordinal = Interlocked.Increment(ref GatedSagaGate.EnteredCount);
        if (ordinal == 1)
        {
            GatedSagaGate.FirstEntered.TrySetResult();
        }
        else
        {
            GatedSagaGate.SecondEntered.TrySetResult();
        }
        await GatedSagaGate.Release.Task;
        Dispatch(new SagaTrackerCommand(edictEvent.WorkflowId));
    }
}
