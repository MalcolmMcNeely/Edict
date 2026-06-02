namespace Edict.Core.Sagas;

/// <summary>
/// The saga terminal-state decision table as a pure, total function, mirroring
/// the pure <c>OutboxSlice</c> transitions: it encodes the entire "what happens
/// next" contract over (state, trigger) and changes rarely. No Orleans, no I/O.
/// <para>
/// The dedup-duplicate check is upstream, so <see cref="SagaTrigger.DuplicateEvent"/>
/// always resolves to <see cref="SagaTransitionDecision.SuppressDuplicate"/>
/// regardless of terminal state. Only a <see cref="SagaTrigger.NewEvent"/>
/// against a terminal state dead-letters. A <see cref="SagaTrigger.CapFired"/>
/// or <see cref="SagaTrigger.CompleteCalled"/> against an already-terminal state
/// is a <see cref="SagaTransitionDecision.NoOp"/> — the guard that makes a
/// non-transactional reminder double-fire a clean no-op.
/// </para>
/// </summary>
static class SagaLifecycleTransition
{
    public static SagaTransitionDecision Resolve(SagaLifecycleState state, SagaTrigger trigger)
    {
        if (trigger == SagaTrigger.DuplicateEvent)
        {
            return SagaTransitionDecision.SuppressDuplicate;
        }

        if (state != SagaLifecycleState.Live)
        {
            return trigger == SagaTrigger.NewEvent
                ? SagaTransitionDecision.DeadLetterTerminal
                : SagaTransitionDecision.NoOp;
        }

        return trigger switch
        {
            SagaTrigger.NewEvent => SagaTransitionDecision.Handle,
            SagaTrigger.CapFired => SagaTransitionDecision.RunTimeoutThenTerminal,
            SagaTrigger.CompleteCalled => SagaTransitionDecision.MarkCompleted,
            _ => SagaTransitionDecision.NoOp,
        };
    }
}
