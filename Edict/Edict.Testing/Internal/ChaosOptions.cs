namespace Edict.Testing.Internal;

/// <summary>
/// Seeded, deterministic chaos applied to the in-process Test Framework's
/// event delivery. Always-on per the framework's no-opt-out chaos contract:
/// production streams redeliver <em>and</em> reorder, so making both default
/// test conditions catches consumers that quietly rely on exactly-once or
/// strict event order. The single <see cref="Seed"/> seeds two independent
/// RNG streams (duplicate, reorder) so tuning one probability does not
/// re-baseline tests gated by the other.
/// <see cref="InvocationsEnabled"/> stays off so a consumer's mock-call-count
/// assertions on an Event Handler are deterministic — note this gate applies
/// only to duplicate redelivery; reorder is invariant under call counts and
/// is always on for handlers too.
/// </summary>
sealed record ChaosOptions(
    int Seed,
    double DuplicateProbability,
    int MaxExtraDeliveries,
    double ReorderProbability,
    int MaxReorderDistance,
    bool InvocationsEnabled)
{
    public static ChaosOptions Default { get; } = new(
        Seed: 0xED1C7,
        DuplicateProbability: 0.5,
        MaxExtraDeliveries: 1,
        ReorderProbability: 0.3,
        MaxReorderDistance: 2,
        InvocationsEnabled: false);
}
