namespace Edict.Testing;

/// <summary>
/// Seeded, deterministic chaos applied to the in-process Test Framework's
/// event delivery (#52 / #53 AC). On by default so every consumer workflow
/// snapshot doubles as an idempotency proof — production streams redeliver, so
/// making redelivery the default test condition catches consumers that quietly
/// rely on exactly-once. <see cref="EdictTestAppBuilder.WithoutChaos"/>
/// disables it; <see cref="EdictTestAppBuilder.WithChaosSeed"/> overrides the
/// seed. Internal: no consumer types it directly (ADR 0017 brand rule).
/// </summary>
sealed record ChaosOptions(bool Enabled, int Seed, double DuplicateProbability, int MaxExtraDeliveries)
{
    /// <summary>The shipped default: chaos on, fixed seed, half of all
    /// publishes are duplicated once. The fixed default makes the Verify
    /// snapshot stable run-to-run; the duplicate redelivery exercises the
    /// dedup ring (ADR 0002) on every consumer test for free.</summary>
    public static ChaosOptions Default { get; } = new(
        Enabled: true,
        Seed: 0xED1C7,
        DuplicateProbability: 0.5,
        MaxExtraDeliveries: 1);

    public static ChaosOptions Off { get; } = Default with { Enabled = false };
}
