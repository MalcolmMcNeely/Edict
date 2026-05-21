namespace Edict.Contracts.TableStorage;

/// <summary>
/// Read-only access to a projection table. The Azure-Table implementation lives in
/// <c>Edict.Core</c>; this interface is the substitution seam the
/// application tier binds to, mirroring <c>IEdictSender</c> for the command side.
/// Point-get and partition-scoped query only — no change-feed or push.
/// </summary>
public interface IEdictTableRepository<T> where T : class
{
    Task<T?> GetAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> QueryPartitionAsync(string partitionKey, CancellationToken cancellationToken = default);
}
