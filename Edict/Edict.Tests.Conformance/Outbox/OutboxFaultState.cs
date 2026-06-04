namespace Edict.Tests.Conformance.Outbox;

/// <summary>
/// Per-fixture fault switch for <see cref="ControllableOutboxExecutor"/>. Each
/// fixture owns one instance and hands it to its silo's executor, so a scenario
/// flips this fixture's switch without touching any other fixture's cluster —
/// the cross-fixture-shape race a process-wide flag would create is impossible by
/// construction.
/// </summary>
public sealed class OutboxFaultState
{
    public volatile bool ShouldFail;

    public int FailedAttempts;

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
        FailedAttempts = 0;
        FailureKind = ControllableFailureKind.InvalidOperation;
    }
}
