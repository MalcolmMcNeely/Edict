namespace Edict.Testing.Internal;

/// <summary>
/// Seeded, deterministic chaos applied to the in-process Test Framework's
/// event delivery. Always-on per the framework's no-opt-out chaos contract:
/// production streams redeliver <em>and</em> reorder, so making both default
/// test conditions catches consumers that quietly rely on exactly-once or
/// strict event order.
/// <para>
/// Two behaviours model the at-least-once contract:
/// <list type="bullet">
///   <item><b>Duplicate redelivery</b> — gated by <see cref="DuplicateProbability"/>
///   with up to <see cref="MaxExtraDeliveries"/> extra deliveries per emission,
///   so the consumer dedup ring is exercised in every multi-step test.</item>
///   <item><b>Bounded reorder</b> — gated by <see cref="ReorderProbability"/>
///   with held-queue depth capped at <see cref="MaxReorderDistance"/>,
///   per-subscriber and per-aggregate, so consumers exercise the
///   reorder-tolerance contract.</item>
/// </list>
/// </para>
/// The single <see cref="Seed"/> seeds two independent RNG streams (duplicate,
/// reorder) via XOR constants so tuning one probability does not re-baseline
/// tests gated by the other.
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
