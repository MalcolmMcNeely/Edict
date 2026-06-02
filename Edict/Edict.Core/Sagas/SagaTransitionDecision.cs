namespace Edict.Core.Sagas;

/// <summary>The decision side of <see cref="SagaLifecycleTransition"/> — what the engine does next.</summary>
enum SagaTransitionDecision
{
    /// <summary>Run the consumer's handler.</summary>
    Handle,

    /// <summary>Drop the redelivery; it was already handled.</summary>
    SuppressDuplicate,

    /// <summary>Dead-letter a new Event that arrived after the saga went terminal.</summary>
    DeadLetterTerminal,

    /// <summary>Run the timeout path, then mark the saga <see cref="SagaLifecycleState.TimedOut"/>.</summary>
    RunTimeoutThenTerminal,

    /// <summary>Mark the saga <see cref="SagaLifecycleState.Completed"/>.</summary>
    MarkCompleted,

    /// <summary>Do nothing — an idempotent re-trigger against an already-terminal saga.</summary>
    NoOp,
}
