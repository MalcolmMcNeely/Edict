namespace Edict.Contracts.TableStorage;

/// <summary>
/// Framework-internal write-store seam for table projections (ADR 0015). The grain
/// base owns the load→apply→writeback orchestration; this interface keeps the
/// backing store trivial to implement. Not part of the consumer contract surface —
/// application code depends only on <see cref="IEdictTableRepository{T}"/>.
/// </summary>
public interface IEdictTableWriteStore<T> where T : class, new()
{
    Task<T?> GetAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default);

    Task UpsertAsync(string partitionKey, string rowKey, T row, CancellationToken cancellationToken = default);
}
