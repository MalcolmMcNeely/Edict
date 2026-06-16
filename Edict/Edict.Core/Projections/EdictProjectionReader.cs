using System.ComponentModel;

using Edict.Contracts.Commands;
using Edict.Contracts.Projections;
using Edict.Contracts.Routing;

using Microsoft.Extensions.DependencyInjection;

using Orleans;

namespace Edict.Core.Projections;

/// <summary>
/// Facade implementation of <see cref="IEdictProjectionReader{TProjection}"/> for
/// the in-grain State species. Resolves the owning projection grain class from the
/// projection type through the shared <see cref="ProjectionReadRouteResolver"/>,
/// addresses the grain by its routing key, and casts the payload that crosses the
/// grain boundary as <see cref="object"/> back to <typeparamref name="TProjection"/>.
/// Mirrors <see cref="EdictListProjectionReader{TListProjection}"/>; the only
/// difference is the single whole-payload <c>ReadAsync</c> grain call.
/// <para>
/// Registered open-generic, so the constructor takes the container (a public
/// parameter type) and resolves the internal resolver itself.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class EdictProjectionReader<TProjection> : IEdictProjectionReader<TProjection>
    where TProjection : class
{
    readonly ProjectionReadRouteResolver _resolver;
    readonly IGrainFactory _grainFactory;

    public EdictProjectionReader(IServiceProvider serviceProvider)
    {
        _resolver = serviceProvider.GetRequiredService<ProjectionReadRouteResolver>();
        _grainFactory = serviceProvider.GetRequiredService<IGrainFactory>();
    }

    public async Task<EdictProjectionRead<TProjection>> ReadAsync(
        Guid key,
        EdictCursor? after = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var grainClassName = _resolver.Resolve(typeof(TProjection));
        // The projection grain rides the same composed key its events route on, so
        // a read addresses it through the one chokepoint rather than the bare Guid.
        var composedKey = EdictKeyComposer.Compose(null, key.ToString("N"));
        var grain = _grainFactory.GetGrain<IEdictProjectionBuilder>(composedKey, grainClassName);
        var result = await grain.EdictReadAsync(after?.CorrelationId, timeout).ConfigureAwait(false);
        return new EdictProjectionRead<TProjection>((TProjection?)result.Payload, result.Status);
    }
}
