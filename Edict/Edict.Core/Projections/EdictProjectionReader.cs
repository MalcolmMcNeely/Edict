using System.ComponentModel;

using Edict.Contracts.Commands;
using Edict.Contracts.Projections;

using Microsoft.Extensions.DependencyInjection;

using Orleans;

namespace Edict.Core.Projections;

/// <summary>
/// Facade implementation of <see cref="IEdictProjectionReader{TRow}"/>. Resolves
/// the owning projection grain class from the row type, addresses the grain by
/// its routing key (the caller's partition key), and casts the row that crosses
/// the grain boundary as <see cref="object"/> back to <typeparamref name="TRow"/>.
/// Mirrors <c>EdictSender</c>: the pure routing lives in
/// <see cref="ProjectionReadRouteResolver"/>; this shell owns the Orleans hop.
/// The grain is addressed through the hand-written
/// <see cref="IEdictProjectionBuilder"/> plus the class-name prefix because
/// Orleans codegen never sees the generator-emitted grain interface.
/// <para>
/// Registered open-generic, so the constructor takes the container (a public
/// parameter type) and resolves the internal resolver itself.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class EdictProjectionReader<TRow> : IEdictProjectionReader<TRow>
    where TRow : class
{
    readonly ProjectionReadRouteResolver _resolver;
    readonly IGrainFactory _grainFactory;

    public EdictProjectionReader(IServiceProvider serviceProvider)
    {
        _resolver = serviceProvider.GetRequiredService<ProjectionReadRouteResolver>();
        _grainFactory = serviceProvider.GetRequiredService<IGrainFactory>();
    }

    public async Task<EdictProjectionRead<TRow>> GetAsync(
        string partitionKey,
        string rowKey,
        EdictCursor? after = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var grain = ResolveGrain(partitionKey);
        var result = await grain.EdictReadRowAsync(rowKey, after?.CorrelationId, timeout).ConfigureAwait(false);
        return new EdictProjectionRead<TRow>((TRow?)result.Payload, result.Status);
    }

    public async Task<EdictProjectionPartitionRead<TRow>> QueryPartitionAsync(
        string partitionKey,
        EdictCursor? after = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var grain = ResolveGrain(partitionKey);
        var result = await grain.EdictReadPartitionAsync(after?.CorrelationId, timeout).ConfigureAwait(false);
        return new EdictProjectionPartitionRead<TRow>(result.Payload.Cast<TRow>().ToList(), result.Status);
    }

    IEdictProjectionBuilder ResolveGrain(string partitionKey)
    {
        var grainClassName = _resolver.Resolve(typeof(TRow));
        return _grainFactory.GetGrain<IEdictProjectionBuilder>(Guid.Parse(partitionKey), grainClassName);
    }
}
