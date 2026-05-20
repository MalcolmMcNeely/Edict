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
    Assembly? _consumerAssembly;
    ChaosOptions _chaos = ChaosOptions.Default;

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

    internal Assembly ConsumerAssembly =>
        _consumerAssembly ?? throw new InvalidOperationException(
            "EdictTestApp needs a consumer assembly: call WithConsumer(typeof(SomeCommandHandler).Assembly).");

    internal ChaosOptions Chaos => _chaos;
}
