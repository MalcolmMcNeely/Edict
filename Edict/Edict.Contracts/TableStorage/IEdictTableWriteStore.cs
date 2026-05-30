using System.ComponentModel;

namespace Edict.Contracts.TableStorage;

/// <summary>
/// Framework-internal write-store seam for table projections. The grain
/// base owns the load→apply→writeback orchestration; this interface keeps the
/// backing store trivial to implement. Application code depends only on
/// <see cref="IEdictTableRepository{T}"/>.
/// <para>
/// Stays <c>public</c> only because the consumer-facing
/// <c>EdictTableProjectionBuilder&lt;T&gt;</c> ctor takes
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

    Task UpsertAsync(string partitionKey, string rowKey, T row, CancellationToken cancellationToken = default);
}
