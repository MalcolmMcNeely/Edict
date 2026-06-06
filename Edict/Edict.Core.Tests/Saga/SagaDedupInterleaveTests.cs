using Edict.Core.Idempotency;

using Xunit;

namespace Edict.Core.Tests.Saga;

// Deterministic white-box proof that the saga dedup path closes the
// concurrent-redelivery window: a handler parked mid-flight holds the in-flight
// claim, so a same-EventId redelivery that interleaves under [AlwaysInterleave] is
// suppressed at the claim. The saga advances once and dispatches its outbound
// command once. Red before the four-path fix (the bare Contains gate let both
// deliveries through, advancing the saga twice and double-dispatching).
[Collection(SagaLifecycleCollection.Name)]
public sealed class SagaDedupInterleaveTests
{
    readonly SagaLifecycleClusterFixture _fixture;

    public SagaDedupInterleaveTests(SagaLifecycleClusterFixture fixture)
    {
        _fixture = fixture;
        GatedSagaGate.Reset();
        CapturingSendCommandExecutor.Reset();
    }

    [Fact]
    public async Task ConcurrentRedelivery_OfSameEventId_AdvancesAndDispatchesExactlyOnce()
    {
        // Arrange
        var workflowId = Guid.NewGuid();
        var consumer = _fixture.GrainFactory.GetGrain<IEdictEventConsumer>(
            workflowId, grainClassNamePrefix: typeof(GatedSaga).FullName);
        var edictEvent = new GatedSagaEvent(workflowId) { EventId = Guid.NewGuid() };

        // Act — delivery A parks inside the handler; delivery B redelivers the same
        // EventId while A is mid-flight. The release is held until B has either
        // entered the handler (unfixed: it raced past the dedup gate) or completed
        // (fixed: it was suppressed at the claim), so the interleave is deterministic
        // with no timing window. Then the gate releases and both turns drain.
        var first = consumer.OnEdictEventAsync(edictEvent);
        await GatedSagaGate.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = consumer.OnEdictEventAsync(edictEvent);
        await Task.WhenAny(GatedSagaGate.SecondEntered.Task, second).WaitAsync(TimeSpan.FromSeconds(10));
        GatedSagaGate.Release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        Assert.Equal(1, GatedSagaGate.EnteredCount);
        var command = Assert.Single(CommandsFor(workflowId));
        Assert.Equal(workflowId, command.WorkflowId);
    }

    static IReadOnlyList<SagaTrackerCommand> CommandsFor(Guid workflowId) =>
        CapturingSendCommandExecutor.Captured
            .OfType<SagaTrackerCommand>()
            .Where(command => command.WorkflowId == workflowId)
            .ToList();
}
