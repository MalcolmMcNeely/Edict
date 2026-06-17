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

    public int FailedWrites;

    public void Reset()
    {
        ShouldFailWrites = false;
        FailUntilWrite = null;
        FailedWrites = 0;
    }
}
