using System.Diagnostics;
using System.Reflection;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Routing;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core.ClaimCheck;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
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

        services.AddValidatorsFromAssemblies(materialised);

        services.AddSingleton(new CommandRouteResolver(routes));
        services.AddSingleton<IEventStreamAccessors>(new EventStreamAccessors(accessors));
        services.AddSingleton<IEdictSender, EdictSender>();
        services.AddSingleton(EdictDiagnostics.ActivitySource);

        // Forensic dead-letter repository is auto-wired so the framework's
        // no-silent-loss guarantee holds without consumer configuration.
        // The provider plugs IEdictTableRepository<EdictDeadLetterEntry>:
        // Edict.Testing's in-memory store factory in tests, Edict.Azure's table
        // repository in production. The framework-shipped projection grain
        // (EdictDeadLetterProjectionBuilder) is discovered by Orleans via the
        // Edict.Core assembly reference; no further registration is required.
        // Factory-delegate registration so a host that wires the framework but
        // has no dead-letter table seam still constructs its DI container —
        // the dependency is only resolved when an operator actually queries
        // the repository (mirrors UpsertRowExecutor / DeadLetterPromoter's lazy
        // service-provider lookup).
        services.AddSingleton<IEdictDeadLetterRepository>(sp =>
            new TableBackedDeadLetterRepository(
                sp.GetRequiredService<IEdictTableRepository<EdictDeadLetterEntry>>()));

        // Receiver-side claim-check unwrap. Every
        // EdictIdempotencyBase consumer resolves this on the stream-observer
        // path, so the framework's front door is the right home — independent
        // of whether the host opted into AddEdictOutbox for publisher-side
        // policy. The IEdictClaimCheckStore is optional: a host with no store
        // still passes non-envelope and inline-payload events through.
        // EdictDeadLetterProjectionBuilder is the one framework consumer
        // for which the fetch is suppressed — the dead-letter row stores
        // the pointer instead of inflating a >32 KB body into a 32 KB
        // Azure Table property. Every other consumer fetches by default.
        services.TryAddSingleton(sp => new ClaimCheckUnwrap(
            sp.GetRequiredService<Serializer>(),
            sp.GetService<IEdictClaimCheckStore>(),
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
