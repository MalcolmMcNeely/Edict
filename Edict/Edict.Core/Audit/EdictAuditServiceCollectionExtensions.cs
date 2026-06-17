using Edict.Contracts.Audit;
using Edict.Core.Tenancy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Orleans.Serialization;

namespace Edict.Core.Audit;

/// <summary>
/// Registers the audit edge resolver: the seam that yields the
/// <see cref="EdictPrincipal"/> for an originating send. Registering a resolver
/// also turns on origin stamping for the provider it lands in, so an Orleans
/// client that issues commands stamps its own origins without a separate switch.
/// </summary>
public static class EdictAuditServiceCollectionExtensions
{
    /// <summary>
    /// Registers an edge resolver that receives the <see cref="IServiceProvider"/>
    /// so it can read an ambient identity seam (an <c>IHttpContextAccessor</c>, a
    /// custom accessor) at resolve time. The delegate returns <see langword="null"/>
    /// when no principal is in scope; at a consumer origin send that fails closed.
    /// </summary>
    public static IServiceCollection AddEdictAudit(
        this IServiceCollection services,
        Func<IServiceProvider, EdictPrincipal?> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        services.TryAddSingleton<EdictAuditEnabledMarker>();
        services.TryAddSingleton<IEdictPrincipalResolver>(serviceProvider =>
            new DelegatePrincipalResolver(serviceProvider, resolver));
        return services;
    }

    /// <summary>
    /// Convenience overload for a resolver that closes over its own captured
    /// state and needs no service provider.
    /// </summary>
    public static IServiceCollection AddEdictAudit(
        this IServiceCollection services,
        Func<EdictPrincipal?> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return services.AddEdictAudit(_ => resolver());
    }

    /// <summary>
    /// Registers the read-only <see cref="IEdictAuditRepository"/> over the
    /// <see cref="IEdictAuditStore"/> and <see cref="IEdictAuditPayloadStore"/>
    /// already in the container, for a process that is not a silo: an Orleans
    /// client or web host that queries the audit log a silo captured. The
    /// silo's <c>WithAudit()</c> registers the same repository; this is the
    /// client-side counterpart, so the stores (e.g. via
    /// <c>AddEdictPostgresAuditReader</c>) must be registered first, and an Orleans
    /// <c>Serializer</c> must be in the container to type a captured body back
    /// (an Orleans client host already registers one).
    /// </summary>
    public static IServiceCollection AddEdictAuditReader(this IServiceCollection services)
    {
        services.TryAddSingleton<IEdictAuditRepository>(serviceProvider =>
            new EdictDefaultAuditRepository(
                serviceProvider.GetRequiredService<IEdictAuditStore>(),
                serviceProvider.GetRequiredService<IEdictAuditPayloadStore>(),
                serviceProvider.GetRequiredService<Serializer>()));
        AddTenantScopedAuditReader(services);
        return services;
    }

    // Registers the ambient-tenant-scoped read surface over the operator repository.
    // It resolves the tenant resolver optionally: with tenancy off there is no resolver
    // and the reader fails closed on use, which is correct — a tenant-scoped read makes
    // no sense without an ambient tenant.
    internal static void AddTenantScopedAuditReader(IServiceCollection services) =>
        services.TryAddSingleton<IEdictTenantScopedAuditRepository>(serviceProvider =>
            new EdictTenantScopedAuditRepository(
                serviceProvider.GetRequiredService<IEdictAuditRepository>(),
                serviceProvider.GetService<IEdictTenantResolver>()));
}
