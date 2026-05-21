namespace Edict.Testing.Chaos;

/// <summary>
/// Seeded, deterministic chaos applied to the in-process Test Framework's
/// event delivery (#52 / #53 AC). On by default so every consumer workflow
/// snapshot doubles as an idempotency proof — production streams redeliver, so
/// making redelivery the default test condition catches consumers that quietly
/// rely on exactly-once. <see cref="EdictTestAppBuilder.WithoutChaos"/>
/// disables it; <see cref="EdictTestAppBuilder.WithChaosSeed"/> overrides the
/// seed. <see cref="InvocationsEnabled"/> is the per-axis gate for
/// <c>EdictEventHandler</c> deliveries: default
/// <b>off</b> so a consumer's mock-call-count assertions are deterministic
/// before the consumer has reasoned about chaos; opt-in via
/// <see cref="EdictTestAppBuilder.WithChaosForInvocations"/>. Internal: no
/// consumer types it directly (brand rule).
/// </summary>
sealed record ChaosOptions(
    bool Enabled,
    int Seed,
    double DuplicateProbability,
    int MaxExtraDeliveries,
    bool InvocationsEnabled)
{
    /// <summary>The shipped default: chaos on, fixed seed, half of all
    /// publishes are duplicated once, but <see cref="EdictEventHandler"/>
    /// deliveries are excluded so a consumer's first event-handler test
    /// surfaces deterministic mock counts. The fixed default
    /// keeps the Verify snapshot stable run-to-run; the duplicate redelivery
    /// exercises the dedup ring on every saga/projection test
    /// for free.</summary>
    public static ChaosOptions Default { get; } = new(
        Enabled: true,
        Seed: 0xED1C7,
        DuplicateProbability: 0.5,
        MaxExtraDeliveries: 1,
        InvocationsEnabled: false);

    public static ChaosOptions Off { get; } = Default with { Enabled = false };
}
