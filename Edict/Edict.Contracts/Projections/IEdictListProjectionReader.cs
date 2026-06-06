using Edict.Contracts.Commands;

namespace Edict.Contracts.Projections;

/// <summary>
/// Reads a List projection's read-model rows from its external keyed store. The
/// application tier binds to this seam, mirroring <see cref="Sending.IEdictSender"/>
/// on the command side. The implementation routes through the projection grain
/// (not the backing store directly), so the read API carries no storage detail:
/// switching a projection's backing store never changes how it is read. For a
/// small per-aggregate read model kept in grain state, read through
/// <see cref="IEdictProjectionReader{TProjection}"/> instead.
/// <para>
/// <paramref name="partitionKey"/> is the projection's routing key — for a
/// per-aggregate projection this is the aggregate's <c>[EdictRouteKey]</c> Guid,
/// which is also its store partition. Point-get and partition-scoped query only.
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
/// reached or the wait timed out (still returning the latest available rows).
/// </para>
/// </summary>
public interface IEdictListProjectionReader<TListProjection> where TListProjection : class
{
    /// <summary>
    /// Point-gets the row at (<paramref name="partitionKey"/>, <paramref name="rowKey"/>),
    /// optionally waiting for <paramref name="after"/> first.
    /// </summary>
    Task<EdictProjectionRead<TListProjection>> GetAsync(
        string partitionKey,
        string rowKey,
        EdictCursor? after = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every row in <paramref name="partitionKey"/>, optionally waiting for
    /// <paramref name="after"/> first.
    /// </summary>
    Task<EdictProjectionPartitionRead<TListProjection>> QueryPartitionAsync(
        string partitionKey,
        EdictCursor? after = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
