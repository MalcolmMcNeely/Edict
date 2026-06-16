using Edict.Contracts.Commands;

namespace Edict.Contracts.Projections;

/// <summary>
/// Reads a tenant-scoped List projection, scoped to the caller's ambient tenant by
/// construction. Where <see cref="IEdictListProjectionReader{TListProjection}"/> takes
/// a partition key, this reader takes none: the framework composes the ambient tenant
/// (the same edge resolver that stamps a send) into the partition, so a consumer
/// queries only "my own" rows and literally cannot express another tenant's partition.
/// The headline read — "list my employees" — binds here; the same query under a
/// different tenant returns empty structurally, not by a permission check. Within-tenant
/// authorization (which row this user may see) stays the consumer's job.
/// <para>
/// Read-your-writes and the <see cref="EdictReadStatus"/> on the result behave exactly
/// as on <see cref="IEdictListProjectionReader{TListProjection}"/>. A tenant-scoped read
/// with no ambient tenant fails closed rather than reading a default partition.
/// </para>
/// </summary>
public interface IEdictTenantScopedListProjectionReader<TListProjection> where TListProjection : class
{
    /// <summary>
    /// Point-gets the row at <paramref name="rowKey"/> within the caller's own tenant
    /// partition, optionally waiting for <paramref name="after"/> first.
    /// </summary>
    Task<EdictProjectionRead<TListProjection>> GetMyAsync(
        string rowKey,
        EdictCursor? after = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every row in the caller's own tenant partition, optionally waiting for
    /// <paramref name="after"/> first. The consumer supplies no tenant; the framework
    /// composes the ambient one.
    /// </summary>
    Task<EdictProjectionPartitionRead<TListProjection>> QueryMyPartitionAsync(
        EdictCursor? after = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
