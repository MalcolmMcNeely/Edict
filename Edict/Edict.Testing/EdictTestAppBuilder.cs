using System.Reflection;

using Edict.Testing.Internal;

using Microsoft.Extensions.DependencyInjection;

namespace Edict.Testing;

/// <summary>
/// Configures an <see cref="EdictTestApp"/>. The consumer's grain assembly is
/// the only required input — Edict is auto-wired from it (the generated route
/// map plus the real Outbox/saga engine), so consumer code is identical under
/// test and in production. Chaos delivery is implicit and always on; tests
/// cannot opt out.
/// </summary>
public sealed class EdictTestAppBuilder
{
    internal const int DefaultClaimCheckThresholdBytes = 30_720;

    Assembly? _consumerAssembly;
    readonly List<Action<IServiceCollection>> _replacements = new();

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
    /// Registers <paramref name="fake"/> as the resolved implementation of
    /// <typeparamref name="TService"/> on both the silo and client containers.
    /// Performs last-<c>AddSingleton</c>-wins, so any previous registration of
    /// the same service type (e.g. the consumer's default) is overridden for
    /// this test. Use this to swap a consumer-injected collaborator — for
    /// example an <c>IEmailNotifier</c> an Event Handler depends on — with a
    /// recording or stubbed substitute. Grain implementations are <b>not</b>
    /// swappable through this seam; they are framework-owned.
    /// </summary>
    public EdictTestAppBuilder Replace<TService>(TService fake) where TService : class
    {
        ArgumentNullException.ThrowIfNull(fake);
        _replacements.Add(services => services.AddSingleton(typeof(TService), fake));
        return this;
    }

    internal Assembly ConsumerAssembly =>
        _consumerAssembly ?? throw new InvalidOperationException(
            "EdictTestApp needs a consumer assembly: call WithConsumer(typeof(SomeCommandHandler).Assembly).");

    internal IReadOnlyList<Action<IServiceCollection>> Replacements => _replacements;
}
