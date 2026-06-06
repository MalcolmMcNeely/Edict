using Edict.Contracts.Commands;

namespace Edict.Contracts.Projections;

/// <summary>
/// Reads an in-grain State projection's read model from the grain's own durable
/// state. The application tier binds to this seam, mirroring
/// <see cref="Sending.IEdictSender"/> on the command side. The implementation
/// routes through the projection grain that owns the read model, so the read API
/// carries no storage detail. For a large or unbounded read model kept in an
/// external store, read through <see cref="IEdictListProjectionReader{TListProjection}"/>
/// instead.
/// <para>
/// <paramref name="key"/> is the projection's routing key — for a per-aggregate
/// projection this is the aggregate's <c>[EdictRouteKey]</c> Guid.
/// </para>
/// <para>
/// Read-your-writes: pass the <see cref="EdictCursor"/> from a command's
/// <c>Accepted</c> result as <paramref name="after"/> to wait, briefly and
/// boundedly, until the work the command set in motion is visible. With no cursor
/// the read answers immediately (the poll path). An omitted
/// <paramref name="timeout"/> falls back to the bounded
/// <c>EdictOptions.ProjectionReadTimeout</c>; pass
/// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to wait indefinitely.
/// The read never throws for eventual-consistency lag — the
/// <see cref="EdictReadStatus"/> on the result reports whether the cursor was
/// reached or the wait timed out (still returning the latest available value).
/// </para>
/// </summary>
public interface IEdictProjectionReader<TProjection> where TProjection : class
{
    /// <summary>
    /// Reads the whole in-grain projection at <paramref name="key"/>, optionally
    /// waiting for <paramref name="after"/> first.
    /// </summary>
    Task<EdictProjectionRead<TProjection>> ReadAsync(
        Guid key,
        EdictCursor? after = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
