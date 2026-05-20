using System.Diagnostics;
using System.Reflection;

using Edict.Contracts.Sending;
using Edict.Core.Commands;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Edict.Core;

/// <summary>
/// Hand-authored DI front door for Edict (ADR 0021). Discoverable the moment
/// the package is referenced — IntelliSense and F12 land here, not in a
/// generator-emitted phantom. The generator's per-assembly
/// <c>EdictRouteRegistrar</c> contributes routes; <c>AddEdict()</c> walks the
/// candidate assemblies for <c>[assembly: EdictRoutes]</c> and stitches the
/// runtime route map.
/// </summary>
public static class EdictServiceCollectionExtensions
{
    /// <summary>
    /// Scans <see cref="AppDomain.CurrentDomain"/> for assemblies annotated
    /// with <c>[assembly: EdictRoutes]</c> and registers the resulting route
    /// map, <see cref="IEdictSender"/>, and the Edict <see cref="ActivitySource"/>.
    /// </summary>
    public static IServiceCollection AddEdict(this IServiceCollection services) =>
        AddEdictCore(
            services,
            AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic),
            requireAttribute: false);

    /// <summary>
    /// Deterministic overload for test contexts and plugin scenarios: each
    /// supplied assembly is expected to carry <c>[assembly: EdictRoutes]</c>,
    /// or <c>AddEdict()</c> throws.
    /// </summary>
    public static IServiceCollection AddEdict(this IServiceCollection services, params Assembly[] assemblies) =>
        AddEdictCore(services, assemblies, requireAttribute: true);

    static IServiceCollection AddEdictCore(IServiceCollection services, IEnumerable<Assembly> assemblies, bool requireAttribute)
    {
        var logger = ResolveStartupLogger(services);
        var discovered = RouteDiscovery.Discover(assemblies, requireAttribute, logger);
        var routes = new Dictionary<Type, CommandRoute>(discovered);

        services.AddSingleton(new CommandRouteResolver(routes));
        services.AddSingleton<IEdictSender, EdictSender>();
        services.AddSingleton(EdictDiagnostics.ActivitySource);
        return services;
    }

    static ILogger ResolveStartupLogger(IServiceCollection services)
    {
        var factoryDescriptor = services
            .LastOrDefault(d => d.ServiceType == typeof(ILoggerFactory));
        if (factoryDescriptor?.ImplementationInstance is ILoggerFactory instance)
        {
            return instance.CreateLogger(typeof(EdictServiceCollectionExtensions).FullName!);
        }
        return NullLogger.Instance;
    }
}
