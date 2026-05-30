using System.Diagnostics;
using System.Reflection;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Contracts.Routing;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core.ClaimCheck;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
using Edict.Core.Metrics;
using Edict.Core.Outbox;
using Edict.Telemetry;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Orleans.Serialization;

namespace Edict.Core;

/// <summary>
/// Hand-authored DI front door for Edict. Discoverable the moment
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
        var materialised = assemblies as IReadOnlyCollection<Assembly> ?? assemblies.ToArray();
        var discovered = RouteDiscovery.Discover(materialised, requireAttribute, logger);
        var routes = new Dictionary<Type, CommandRoute>(discovered);

        var discoveredAccessors = EventStreamAccessorDiscovery.Discover(materialised, logger);
        var accessors = new Dictionary<Type, EdictEventStreamAccessor>(discoveredAccessors);

        var discoveredTagWriters = EventTagWritersDiscovery.Discover(materialised, logger);
        var tagWriters = new Dictionary<Type, Action<EdictEvent, Activity>>(discoveredTagWriters);

        // EdictDeadLetterRaised lives in Edict.Contracts where the Edict
        // generator does not run, so the per-assembly registrar mechanism
        // cannot contribute its accessor. Hand-register it: the framework
        // owns this event and its stream + route key are statically known.
        accessors[typeof(EdictDeadLetterRaised)] = new EdictEventStreamAccessor(
            "edict-dead-letter",
            static edictEvent => ((EdictDeadLetterRaised)edictEvent).SingletonKey);

        services.AddValidatorsFromAssemblies(materialised);

        services.AddSingleton(new CommandRouteResolver(routes));
        services.AddSingleton<IEventStreamAccessors>(new EventStreamAccessors(accessors));
        services.AddSingleton<IEventTagWriters>(new EventTagWriters(tagWriters));
        services.AddSingleton<IEdictSender>(serviceProvider => new EdictSender(
            serviceProvider.GetRequiredService<CommandRouteResolver>(),
            serviceProvider.GetRequiredService<IGrainFactory>()));
        services.AddSingleton(EdictDiagnostics.ActivitySource);

        // TryAdd so an assertable variant (the Edict.Testing rig) wins via
        // the same swap-seam pattern as IEdictSender.
        services.TryAddSingleton<IEdictMetricsCache>(serviceProvider =>
            new EdictMetricsCache(serviceProvider.GetRequiredService<TimeProvider>()));

        // Factory-delegate registration so a host that wires the framework
        // but has no dead-letter table seam still constructs its DI
        // container — the IEdictTableRepository<EdictDeadLetterEntry>
        // dependency only resolves when an operator queries the repository.
        services.AddSingleton<IEdictDeadLetterRepository>(serviceProvider =>
            new TableBackedDeadLetterRepository(
                serviceProvider.GetRequiredService<IEdictTableRepository<EdictDeadLetterEntry>>()));

        // Receiver-side wiring lives on the framework's front door rather
        // than AddEdictOutbox: every EdictIdempotencyBase consumer resolves
        // this on the stream-observer path, independent of publisher-side
        // policy. The store is optional — a host with no store still passes
        // non-envelope and inline-payload events through.
        // EdictDeadLetterProjectionBuilder is the one consumer for which
        // the fetch is suppressed — the dead-letter row stores the pointer,
        // not the rehydrated event.
        services.TryAddSingleton(serviceProvider => new ClaimCheckUnwrap(
            serviceProvider.GetRequiredService<Serializer>(),
            serviceProvider.GetService<IEdictClaimCheckStore>(),
            shouldFetchForConsumer: static t => t != typeof(EdictDeadLetterProjectionBuilder)));

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
