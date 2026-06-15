using Edict.Contracts.Audit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
}
