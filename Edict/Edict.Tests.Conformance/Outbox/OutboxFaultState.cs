using Edict.Core.Outbox;

namespace Edict.Tests.Conformance.Outbox;

/// <summary>
/// Per-fixture fault switch for <see cref="ControllableOutboxExecutor"/>. Each
/// fixture owns one instance and hands it to its silo's executor, so a scenario
/// flips this fixture's switch without touching any other fixture's cluster —
/// the cross-fixture-shape race a process-wide flag would create is impossible by
/// construction.
/// <para>
/// The fault is addressable four ways, checked in priority order:
/// <see cref="FailOnlyKind"/> first narrows which effect kind can fault at all,
/// then <see cref="FailEffectIndex"/> (the Nth effect in drain order),
/// <see cref="FailUntilAttempt"/> (the first N attempts), and finally
/// <see cref="ShouldFail"/> as the historical unbounded case.
/// </para>
/// </summary>
public sealed class OutboxFaultState
{
    /// <summary>Unbounded: every intercepted effect faults until cleared.</summary>
    public volatile bool ShouldFail;

    /// <summary>
    /// Count-addressed: fault while fewer than this many attempts have already
    /// failed, then let the effect through — the auto-heal a recovery scenario
    /// converges on without toggling a flag. Null leaves this addressing off.
    /// </summary>
    public int? FailUntilAttempt;

    /// <summary>
    /// Index-addressed: fault only the effect at this zero-based ordinal in drain
    /// order (the order effects enter the executor since the last <see cref="Reset"/>,
    /// which is the <c>Pending</c> FIFO order — the fault throw precedes any await,
    /// so the ordinal is deterministic). Lets a scenario fail one sibling in a
    /// multi-effect batch and leave the rest done. Null leaves this addressing off.
    /// </summary>
    public int? FailEffectIndex;

    /// <summary>
    /// Kind-addressed: restrict faulting to this effect kind, so a scenario can
    /// fault a <see cref="OutboxEffectKind.SendCommand"/> without disturbing the
    /// <see cref="OutboxEffectKind.PublishEvent"/> effects on the same drain. Null
    /// lets any intercepted kind fault.
    /// </summary>
    public OutboxEffectKind? FailOnlyKind;

    public int FailedAttempts;

    /// <summary>
    /// Effects seen since the last <see cref="Reset"/>, the source of the ordinal
    /// <see cref="FailEffectIndex"/> matches against. Incremented per intercepted
    /// effect of the targeted kind.
    /// </summary>
    public int EffectsSeen;

    /// <summary>
    /// Selects the exception kind raised on a failing pass. Defaults to the
    /// historical <see cref="InvalidOperationException"/> so existing scenarios
    /// stay green; new scenarios that need to verify the classifier-to-bucket
    /// mapping for a typed runtime fault switch this to the relevant kind.
    /// </summary>
    public volatile ControllableFailureKind FailureKind = ControllableFailureKind.InvalidOperation;

    public void Reset()
    {
        ShouldFail = false;
        FailUntilAttempt = null;
        FailEffectIndex = null;
        FailOnlyKind = null;
        FailedAttempts = 0;
        EffectsSeen = 0;
        FailureKind = ControllableFailureKind.InvalidOperation;
    }
}
