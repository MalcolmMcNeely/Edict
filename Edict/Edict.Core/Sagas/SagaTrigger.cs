namespace Edict.Core.Sagas;

/// <summary>What moved the saga: the input side of <see cref="SagaLifecycleTransition"/>.</summary>
enum SagaTrigger
{
    /// <summary>An already-handled Event redelivered — the dedup ring matched upstream.</summary>
    DuplicateEvent,

    /// <summary>An Event the dedup ring has not seen.</summary>
    NewEvent,

    /// <summary>The absolute-cap reminder fired.</summary>
    CapFired,

    /// <summary>The consumer called <c>Complete()</c> inside a handler.</summary>
    CompleteCalled,
}
