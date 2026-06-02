using Edict.Core.Sagas;

namespace Edict.Core.Tests.Saga;

public sealed class SagaLifecycleTransitionTests
{
    // SagaTrigger and SagaTransitionDecision are framework-internal, so the
    // decision table is driven from the method body rather than [InlineData]
    // (a public Theory parameter cannot name an internal type). One [Fact] over
    // the whole table keeps it a single behaviour: the full state x trigger map.
    [Fact]
    public void Resolve_ShouldMapTheFullStateAndTriggerTable()
    {
        (SagaLifecycleState State, SagaTrigger Trigger, SagaTransitionDecision Expected)[] table =
        [
            // A duplicate (already-handled redelivery) is suppressed first,
            // regardless of lifecycle state — the dedup ring is upstream.
            (SagaLifecycleState.Live, SagaTrigger.DuplicateEvent, SagaTransitionDecision.SuppressDuplicate),
            (SagaLifecycleState.Completed, SagaTrigger.DuplicateEvent, SagaTransitionDecision.SuppressDuplicate),
            (SagaLifecycleState.TimedOut, SagaTrigger.DuplicateEvent, SagaTransitionDecision.SuppressDuplicate),
            // A genuinely-new Event: handled while live, dead-lettered once terminal.
            (SagaLifecycleState.Live, SagaTrigger.NewEvent, SagaTransitionDecision.Handle),
            (SagaLifecycleState.Completed, SagaTrigger.NewEvent, SagaTransitionDecision.DeadLetterTerminal),
            (SagaLifecycleState.TimedOut, SagaTrigger.NewEvent, SagaTransitionDecision.DeadLetterTerminal),
            // The cap fires once while live; a second fire against a terminal
            // state is a no-op (reminder unregister is not transactional).
            (SagaLifecycleState.Live, SagaTrigger.CapFired, SagaTransitionDecision.RunTimeoutThenTerminal),
            (SagaLifecycleState.Completed, SagaTrigger.CapFired, SagaTransitionDecision.NoOp),
            (SagaLifecycleState.TimedOut, SagaTrigger.CapFired, SagaTransitionDecision.NoOp),
            // Complete() is hard-terminal; calling it again is idempotent.
            (SagaLifecycleState.Live, SagaTrigger.CompleteCalled, SagaTransitionDecision.MarkCompleted),
            (SagaLifecycleState.Completed, SagaTrigger.CompleteCalled, SagaTransitionDecision.NoOp),
            (SagaLifecycleState.TimedOut, SagaTrigger.CompleteCalled, SagaTransitionDecision.NoOp),
        ];

        var mismatches = table
            .Where(row => SagaLifecycleTransition.Resolve(row.State, row.Trigger) != row.Expected)
            .Select(row => $"({row.State}, {row.Trigger}) expected {row.Expected} but got {SagaLifecycleTransition.Resolve(row.State, row.Trigger)}")
            .ToList();

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }
}
