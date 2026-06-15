using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Orleans.Hosting;

namespace Edict.Core.Audit;

/// <summary>
/// Silo-side switch that turns on principal origin stamping. Pairs with
/// <see cref="EdictAuditServiceCollectionExtensions.AddEdictAudit(IServiceCollection, System.Func{IServiceProvider, Contracts.Audit.EdictPrincipal?})"/>:
/// <c>WithAudit()</c> arms the on-switch and the startup validator then fails
/// loudly if no resolver was registered to back it.
/// </summary>
public static class EdictAuditSiloBuilderExtensions
{
    /// <summary>Turns on origin stamping for this silo.</summary>
    public static ISiloBuilder WithAudit(this ISiloBuilder silo)
    {
        silo.Services.TryAddSingleton<EdictAuditEnabledMarker>();
        return silo;
    }
}
