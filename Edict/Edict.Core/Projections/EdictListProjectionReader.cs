using System.ComponentModel;

using Edict.Contracts.Commands;
using Edict.Contracts.Projections;
using Edict.Contracts.Routing;

using Microsoft.Extensions.DependencyInjection;

using Orleans;

namespace Edict.Core.Projections;

/// <summary>
/// Facade implementation of <see cref="IEdictListProjectionReader{TListProjection}"/>.
/// Resolves the owning projection grain class from the row type, addresses the
/// grain by its routing key (the caller's partition key), and casts the row that
/// crosses the grain boundary as <see cref="object"/> back to
/// <typeparamref name="TListProjection"/>. Mirrors <c>EdictSender</c>: the pure
/// routing lives in <see cref="ProjectionReadRouteResolver"/>; this shell owns the
/// Orleans hop. The grain is addressed through the hand-written
/// <see cref="IEdictProjectionBuilder"/> plus the class-name prefix because
/// Orleans codegen never sees the generator-emitted grain interface.
/// <para>
/// Registered open-generic, so the constructor takes the container (a public
/// parameter type) and resolves the internal resolver itself.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class EdictListProjectionReader<TListProjection> : IEdictListProjectionReader<TListProjection>
    where TListProjection : class
{
    readonly ProjectionReadRouteResolver _resolver;
    readonly IGrainFactory _grainFactory;

    public EdictListProjectionReader(IServiceProvider serviceProvider)
    {
        _resolver = serviceProvider.GetRequiredService<ProjectionReadRouteResolver>();
        _grainFactory = serviceProvider.GetRequiredService<IGrainFactory>();
    }

    public async Task<EdictProjectionRead<TListProjection>> GetAsync(
        string partitionKey,
        string rowKey,
        EdictCursor? after = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var grain = ResolveGrain(partitionKey);
        var result = await grain.EdictReadRowAsync(rowKey, after?.CorrelationId, timeout).ConfigureAwait(false);
        return new EdictProjectionRead<TListProjection>((TListProjection?)result.Payload, result.Status);
    }

    public async Task<EdictProjectionPartitionRead<TListProjection>> QueryPartitionAsync(
        string partitionKey,
        EdictCursor? after = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var grain = ResolveGrain(partitionKey);
        var result = await grain.EdictReadPartitionAsync(after?.CorrelationId, timeout).ConfigureAwait(false);
        return new EdictProjectionPartitionRead<TListProjection>(result.Payload.Cast<TListProjection>().ToList(), result.Status);
    }

    IEdictProjectionBuilder ResolveGrain(string partitionKey)
    {
        var grainClassName = _resolver.Resolve(typeof(TListProjection));
        // Fold the partition key through the one composition chokepoint like the State
        // sibling, so a future change to composition reaches every reader uniformly. A
        // public list partition carries no tenant, so this composes to the bare key today.
        var composedKey = EdictKeyComposer.Compose(null, partitionKey);
        return _grainFactory.GetGrain<IEdictProjectionBuilder>(composedKey, grainClassName);
    }
}
