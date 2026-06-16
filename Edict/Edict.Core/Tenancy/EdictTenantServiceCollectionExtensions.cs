using Edict.Contracts.Tenancy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Orleans;

namespace Edict.Core.Tenancy;

/// <summary>
/// Registers the tenant edge resolver: the seam that yields the
/// <see cref="EdictTenantId"/> for an originating send. Registering a resolver is
/// the multi-tenancy opt-in and turns on origin tenant-stamping for the provider
/// it lands in, so an Orleans client that issues commands stamps its own origins.
/// A single-tenant app registers nothing and pays no tenant tax.
/// </summary>
public static class EdictTenantServiceCollectionExtensions
{
    /// <summary>
    /// Registers an edge resolver that receives the <see cref="IServiceProvider"/>
    /// so it can read an ambient identity seam (an <c>IHttpContextAccessor</c>, a
    /// signed tenant claim) at resolve time. The delegate returns
    /// <see langword="null"/> when no tenant is in scope; at a tenant-scoped origin
    /// send that fails closed. The principal-to-tenant mapping is the consumer's,
    /// never read off the request body.
    /// </summary>
    public static IServiceCollection AddEdictTenant(
        this IServiceCollection services,
        Func<IServiceProvider, EdictTenantId?> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        services.TryAddSingleton<EdictTenantEnabledMarker>();
        services.TryAddSingleton<IEdictTenantResolver>(serviceProvider =>
            new DelegateTenantResolver(serviceProvider, resolver));

        // The runtime isolation backstop, present only when tenancy is on. A
        // single-tenant app registers no resolver and pays no per-call filter; the
        // filter is silent for public aggregates even when present. A client build has
        // no incoming grain calls, so the registration is inert there.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IIncomingGrainCallFilter, EdictTenantIsolationCallFilter>());
        return services;
    }

    /// <summary>
    /// Convenience overload for a resolver that closes over its own captured state
    /// and needs no service provider.
    /// </summary>
    public static IServiceCollection AddEdictTenant(
        this IServiceCollection services,
        Func<EdictTenantId?> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return services.AddEdictTenant(_ => resolver());
    }
}
