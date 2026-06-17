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
    bool _auditEnabled;
    bool _tenancyEnabled;
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

    /// <summary>
    /// Turns auditing on for the in-memory app, backed by in-memory audit stores so
    /// no container is needed: a command captures its C1 decision and one E1 record
    /// per raised event, drained deterministically on <see cref="EdictTestApp.Drain"/>
    /// and read back through <see cref="EdictTestApp.Audit"/>. Sends are attributed to
    /// a default test principal so simply turning auditing on does not trip the
    /// origin fail-closed; call <see cref="EdictTestApp.ActAs"/> to attribute
    /// subsequent sends to a specific actor.
    /// </summary>
    public EdictTestAppBuilder WithAudit()
    {
        _auditEnabled = true;
        return this;
    }

    /// <summary>
    /// Turns multi-tenancy on for the in-memory app: registers the tenant edge resolver
    /// and the isolation call filter, so a tenant-scoped aggregate folds its tenant into
    /// the routed key, the headline ambient-scoped reads (<see cref="EdictTestApp.QueryMyTenantPartitionAsync"/>,
    /// <see cref="EdictTestApp.TenantAudit"/>) answer the caller's own partition, and a
    /// stolen route key into another tenant is denied. Drive "act as Acme" with
    /// <see cref="EdictTestApp.RunAsTenant"/>; establish a new tenant explicitly with
    /// <see cref="EdictTestApp.SendAsync(Edict.Contracts.Commands.EdictCommand, Edict.Contracts.Tenancy.EdictTenantId)"/>.
    /// A single-tenant app leaves this off and pays no tenant tax.
    /// </summary>
    public EdictTestAppBuilder WithTenancy()
    {
        _tenancyEnabled = true;
        return this;
    }

    internal Assembly ConsumerAssembly =>
        _consumerAssembly ?? throw new InvalidOperationException(
            "EdictTestApp needs a consumer assembly: call WithConsumer(typeof(SomeCommandHandler).Assembly).");

    internal bool AuditEnabled => _auditEnabled;

    internal bool TenancyEnabled => _tenancyEnabled;

    internal IReadOnlyList<Action<IServiceCollection>> Replacements => _replacements;
}
