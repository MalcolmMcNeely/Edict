using System.Reflection;

using Edict.Testing.Chaos;

namespace Edict.Testing;

/// <summary>
/// Configures an <see cref="EdictTestApp"/>. The consumer's grain assembly is
/// the only required input — Edict is auto-wired from it (the generated route
/// map plus the real Outbox/saga engine), so consumer code is identical under
/// test and in production.
/// </summary>
public sealed class EdictTestAppBuilder
{
    /// <summary>
    /// Mirrors <c>EdictAzureStreamsOptions.ClaimCheckThresholdBytes</c> so the
    /// in-memory test framework exercises the same commit pipeline as
    /// production (ADR 0024). Override per test via
    /// <see cref="WithClaimCheckThresholdBytes"/> to force the path on a
    /// small payload.
    /// </summary>
    public const int DefaultClaimCheckThresholdBytes = 30_720;

    Assembly? _consumerAssembly;
    ChaosOptions _chaos = ChaosOptions.Default;
    int _claimCheckThresholdBytes = DefaultClaimCheckThresholdBytes;

    /// <summary>
    /// The consumer assembly whose grains, commands/events and generated
    /// <c>AddEdict()</c> the in-memory cluster boots. Required.
    /// </summary>
    public EdictTestAppBuilder WithConsumer(Assembly consumerAssembly)
    {
        _consumerAssembly = consumerAssembly;
        return this;
    }

    /// <summary>
    /// Disables the default seeded chaos (duplicate redelivery). Use only when
    /// a test specifically wants the no-redelivery baseline — production
    /// streams redeliver, so leaving chaos on is the better default.
    /// </summary>
    public EdictTestAppBuilder WithoutChaos()
    {
        _chaos = ChaosOptions.Off;
        return this;
    }

    /// <summary>
    /// Overrides the seed of the default chaos policy. Same seed across runs
    /// yields the same delivery pattern, so the Verify snapshot stays stable.
    /// </summary>
    public EdictTestAppBuilder WithChaosSeed(int seed)
    {
        _chaos = _chaos with { Seed = seed };
        return this;
    }

    /// <summary>
    /// Opts <see cref="Edict.Core.EventHandler.EdictEventHandler"/> deliveries
    /// into the duplicate-redelivery chaos that other consumer roles get by
    /// default (ADR 0023, issue #67). Use only when a test specifically wants
    /// to exercise the dedup ring under chaos for an event handler — the
    /// shipped default is off so a consumer's first mock-call-count assertion
    /// is deterministic.
    /// </summary>
    public EdictTestAppBuilder WithChaosForInvocations()
    {
        _chaos = _chaos with { InvocationsEnabled = true };
        return this;
    }

    /// <summary>
    /// Overrides the claim-check byte-length threshold for this test (ADR 0024).
    /// Lower the value to force the path on a small payload — useful when a
    /// test wants to stress the publisher pipeline without raising a 30 KB
    /// event.
    /// </summary>
    public EdictTestAppBuilder WithClaimCheckThresholdBytes(int thresholdBytes)
    {
        _claimCheckThresholdBytes = thresholdBytes;
        return this;
    }

    internal Assembly ConsumerAssembly =>
        _consumerAssembly ?? throw new InvalidOperationException(
            "EdictTestApp needs a consumer assembly: call WithConsumer(typeof(SomeCommandHandler).Assembly).");

    internal ChaosOptions Chaos => _chaos;

    internal int ClaimCheckThresholdBytes => _claimCheckThresholdBytes;
}
