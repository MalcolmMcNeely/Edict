using Edict.Contracts.Audit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Orleans.Hosting;
using Orleans.Serialization;

namespace Edict.Core.Audit;

/// <summary>
/// Silo-side switch that turns on principal origin stamping. Pairs with
/// <see cref="EdictAuditServiceCollectionExtensions.AddEdictAudit(IServiceCollection, System.Func{IServiceProvider, Contracts.Audit.EdictPrincipal?})"/>:
/// <c>WithAudit()</c> arms the on-switch and the startup validator then fails
/// loudly if no resolver was registered to back it.
/// </summary>
public static class EdictAuditSiloBuilderExtensions
{
    /// <summary>
    /// Turns on origin stamping and audit capture for this silo. Arms the
    /// on-switch, marks the silo as capturing (so the startup validator requires a
    /// store), and registers the default <see cref="IEdictAuditRepository"/> over
    /// whatever store the substrate provides.
    /// </summary>
    public static ISiloBuilder WithAudit(this ISiloBuilder silo)
    {
        silo.Services.TryAddSingleton<EdictAuditEnabledMarker>();
        silo.Services.TryAddSingleton<EdictAuditSiloMarker>();
        // Factory form so a silo that turned auditing on but registered no store
        // fails through the wiring validator's clear message, not a raw container
        // validation error at build.
        silo.Services.TryAddSingleton<IEdictAuditRepository>(serviceProvider =>
            new EdictDefaultAuditRepository(
                serviceProvider.GetRequiredService<IEdictAuditStore>(),
                serviceProvider.GetRequiredService<IEdictAuditPayloadStore>(),
                serviceProvider.GetRequiredService<Serializer>()));
        EdictAuditServiceCollectionExtensions.AddTenantScopedAuditReader(silo.Services);
        return silo;
    }
}
