using System.ComponentModel;

using Edict.Contracts.Commands;
using Edict.Contracts.Projections;
using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;
using Edict.Core.Tenancy;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans;

namespace Edict.Core.Projections;

/// <summary>
/// Facade implementation of <see cref="IEdictTenantScopedListProjectionReader{TListProjection}"/>.
/// Resolves the ambient tenant from the same edge resolver the send path uses, composes
/// it into the projection grain key through the one chokepoint, and addresses the grain
/// by that composed key. The consumer never supplies a partition, so a raw partition key
/// cannot bypass the tenant fold: the partition queried is always the caller's own. A
/// missing ambient tenant fails closed rather than reading a default partition.
/// <para>
/// Registered open-generic, so the constructor takes the container (a public parameter
/// type) and resolves the internal resolvers itself.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class EdictTenantScopedListProjectionReader<TListProjection> : IEdictTenantScopedListProjectionReader<TListProjection>
    where TListProjection : class
{
    // A read addresses the tenant partition through a sentinel route key; the grain's
    // partition is parsed from the tenant side of its own composed key, so any route key
    // in the tenant reaches the same store partition. Empty keeps the sentinel obvious.
    static readonly string PartitionSentinelRouteKey = Guid.Empty.ToString("N");

    readonly ProjectionReadRouteResolver _resolver;
    readonly IGrainFactory _grainFactory;
    readonly IEdictTenantResolver? _tenantResolver;

    public EdictTenantScopedListProjectionReader(IServiceProvider serviceProvider)
    {
        _resolver = serviceProvider.GetRequiredService<ProjectionReadRouteResolver>();
        _grainFactory = serviceProvider.GetRequiredService<IGrainFactory>();
        _tenantResolver = serviceProvider.GetService<IEdictTenantResolver>();
    }

    public async Task<EdictProjectionRead<TListProjection>> GetMyAsync(
        string rowKey,
        EdictCursor? after = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(rowKey);
        var grain = ResolveGrain(rowKey);
        var result = await grain.EdictReadRowAsync(rowKey, after?.CorrelationId, timeout).ConfigureAwait(false);
        return new EdictProjectionRead<TListProjection>((TListProjection?)result.Payload, result.Status);
    }

    public async Task<EdictProjectionPartitionRead<TListProjection>> QueryMyPartitionAsync(
        EdictCursor? after = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var grain = ResolveGrain(PartitionSentinelRouteKey);
        var result = await grain.EdictReadPartitionAsync(after?.CorrelationId, timeout).ConfigureAwait(false);
        return new EdictProjectionPartitionRead<TListProjection>(result.Payload.Cast<TListProjection>().ToList(), result.Status);
    }

    IEdictProjectionBuilder ResolveGrain(string routeKey)
    {
        var tenant = _tenantResolver?.Resolve()
            ?? throw EdictMissingTenantException.ForProjectionRead(typeof(TListProjection));
        // Seed the per-turn relay with the resolved tenant so it propagates to the
        // projection grain's storage read, where the Postgres row-security backstop reads
        // it as the SET LOCAL tenant. The relay is the resolved tenant itself, taken before
        // composition, so it is an independent check on the composed key: a key folded for
        // the wrong partition is denied at the database rather than leaking another tenant's
        // rows.
        TenantRelay.Seed(tenant);
        var grainClassName = _resolver.Resolve(typeof(TListProjection));
        var composedKey = EdictKeyComposer.Compose(tenant, routeKey);
        return _grainFactory.GetGrain<IEdictProjectionBuilder>(composedKey, grainClassName);
    }
}
