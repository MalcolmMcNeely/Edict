namespace Edict.Testing.Internal;

/// <summary>
/// Seeded, deterministic chaos applied to the in-process Test Framework's
/// event delivery. Always-on per the framework's no-opt-out chaos contract:
/// production streams redeliver, so making redelivery the default test
/// condition catches consumers that quietly rely on exactly-once.
/// <see cref="InvocationsEnabled"/> stays off so a consumer's mock-call-count
/// assertions on an Event Handler are deterministic.
/// </summary>
sealed record ChaosOptions(
    int Seed,
    double DuplicateProbability,
    int MaxExtraDeliveries,
    bool InvocationsEnabled)
{
    public static ChaosOptions Default { get; } = new(
        Seed: 0xED1C7,
        DuplicateProbability: 0.5,
        MaxExtraDeliveries: 1,
        InvocationsEnabled: false);
}
