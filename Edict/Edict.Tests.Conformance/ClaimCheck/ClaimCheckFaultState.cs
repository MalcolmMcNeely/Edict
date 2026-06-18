namespace Edict.Tests.Conformance.ClaimCheck;

/// <summary>
/// Per-fixture fault switch for <see cref="ControllableClaimCheckStore"/>. Each
/// fixture owns one instance and hands it to its silo's claim-check store, so a
/// scenario flips this fixture's switch without touching any other fixture's
/// cluster — the cross-fixture-shape race a process-wide flag would create is
/// impossible by construction. The claim-check analogue of the outbox and
/// grain-storage fault switches.
/// </summary>
public sealed class ClaimCheckFaultState
{
    /// <summary>
    /// Count-addressed: fault every <c>GetAsync</c> while fewer than this many
    /// fetches have already failed, then let the fetch through — the transient
    /// recovery a scenario converges on without toggling a flag, so the engine's
    /// per-entry retry drives the recovery rather than a manual heal. The body is
    /// present in the store the whole time; only the fetch is held down, which is
    /// what distinguishes this from the permanent-missing dead-letter path. Null
    /// leaves the fault off and every fetch succeeds.
    /// </summary>
    public int? FailFetchUntilAttempt;

    /// <summary>
    /// Scopes the count-addressed fault to a single claim-check key (the event id
    /// the scenario authored and parked the body under). The decorated store is
    /// silo-wide, so an unscoped count is stolen by any peer receiver's fetch that
    /// lands in the armed window — and a receiver fetch is triggered by stream
    /// delivery, not awaited by the producer. Null faults every fetch (the
    /// historical behaviour).
    /// </summary>
    public Guid? FailFetchForEvent;

    public int FailedFetches;

    public void Reset()
    {
        FailFetchUntilAttempt = null;
        FailFetchForEvent = null;
        FailedFetches = 0;
    }
}
