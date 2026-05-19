using System.Reflection;

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

    /// <summary>
    /// The consumer assembly whose grains, commands/events and generated
    /// <c>AddEdict()</c> the in-memory cluster boots. Required.
    /// </summary>
    public EdictTestAppBuilder WithConsumer(Assembly consumerAssembly)
    {
        _consumerAssembly = consumerAssembly;
        return this;
    }

    internal Assembly ConsumerAssembly =>
        _consumerAssembly ?? throw new InvalidOperationException(
            "EdictTestApp needs a consumer assembly: call WithConsumer(typeof(SomeCommandHandler).Assembly).");
}
