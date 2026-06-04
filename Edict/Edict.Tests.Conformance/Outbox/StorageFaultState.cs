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
    public volatile bool ShouldFailWrites;

    public int FailedWrites;

    public void Reset()
    {
        ShouldFailWrites = false;
        FailedWrites = 0;
    }
}
