using System.ComponentModel;

namespace Edict.Contracts.TableStorage;

/// <summary>
/// Framework-internal backing-store seam for table projections. The grain
/// base owns the load→apply→writeback orchestration and serves reads on the
/// consumer's behalf; this interface keeps the backing store trivial to
/// implement. Consumers never type it — they read through the
/// <c>IEdictListProjectionReader&lt;TListProjection&gt;</c> facade, which routes through the grain.
/// <para>
/// Stays <c>public</c> only because the consumer-facing
/// <c>EdictListProjectionBuilder&lt;TListProjection&gt;</c> ctor takes
/// <c>IEdictTableStoreFactory</c>, whose <c>CreateAsync&lt;T&gt;</c> returns
/// this type — flipping it to internal fires CS0050 on a public base ctor's
/// signature. Hidden from consumer IntelliSense because the consumer never
/// types this — the projection-builder base resolves a store on their behalf.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IEdictTableWriteStore<T> where T : class, new()
{
    Task<T?> GetAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> QueryPartitionAsync(string partitionKey, CancellationToken cancellationToken = default);

    Task UpsertAsync(string partitionKey, string rowKey, T row, CancellationToken cancellationToken = default);
}
