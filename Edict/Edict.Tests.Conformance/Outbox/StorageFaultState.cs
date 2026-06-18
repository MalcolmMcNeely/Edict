namespace Edict.Tests.Conformance.Outbox;

/// <summary>
/// Per-fixture fault switch for <see cref="ControllableGrainStorage"/>. Each
/// fixture owns one instance and hands it to its silo's grain-storage decorator,
/// so a scenario flips this fixture's switch without touching any other fixture's
/// cluster — the cross-fixture-shape race a process-wide flag would create is
/// impossible by construction.
/// </summary>
public sealed class StorageFaultState
{
    /// <summary>Unbounded: every grain-state write faults until cleared.</summary>
    public volatile bool ShouldFailWrites;

    /// <summary>
    /// Count-addressed: fault while fewer than this many writes have already
    /// failed, then let the write through — the auto-heal a clean-reload scenario
    /// converges on without toggling a flag, so the framework's drop-and-reactivate
    /// drives the recovery rather than a manual heal between two grain states. Null
    /// leaves this addressing off.
    /// </summary>
    public int? FailUntilWrite;

    /// <summary>
    /// Scopes both fault modes to a single grain, identified by its
    /// <see cref="Orleans.Runtime.GrainId.Key"/> text (an N-format key string). A
    /// count-addressed fault is only deterministic when it counts the grain under
    /// test: the decorated provider is silo-wide, so an unscoped count is stolen by
    /// any peer grain whose write lands in the armed window — most sharply an event
    /// handler's dedup-commit write, which is triggered by stream delivery and is
    /// not awaited by the producing command. Null faults silo-wide (the historical
    /// behaviour, kept for the unbounded case where every write faults so the grain
    /// under test always faults too).
    /// </summary>
    public volatile string? TargetGrainKey;

    public int FailedWrites;

    public void Reset()
    {
        ShouldFailWrites = false;
        FailUntilWrite = null;
        TargetGrainKey = null;
        FailedWrites = 0;
    }
}
