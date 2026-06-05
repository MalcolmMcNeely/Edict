namespace Edict.Contracts.Projections;

/// <summary>
/// Reads a projection's read-model rows. The application tier binds to this
/// seam, mirroring <see cref="Sending.IEdictSender"/> on the command side. The
/// implementation routes through the projection grain (not the backing store
/// directly), so the read API carries no storage detail: switching a
/// projection's backing store never changes how it is read.
/// <para>
/// <paramref name="partitionKey"/> is the projection's routing key — for a
/// per-aggregate projection this is the aggregate's <c>[EdictRouteKey]</c> Guid,
/// which is also its store partition. Point-get and partition-scoped query only.
/// </para>
/// </summary>
public interface IEdictProjectionReader<TRow> where TRow : class
{
    /// <summary>Point-gets the row at (<paramref name="partitionKey"/>, <paramref name="rowKey"/>), or <see langword="null"/>.</summary>
    Task<TRow?> GetAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default);

    /// <summary>Returns every row in <paramref name="partitionKey"/>.</summary>
    Task<IReadOnlyList<TRow>> QueryPartitionAsync(string partitionKey, CancellationToken cancellationToken = default);
}
