using Edict.Contracts.TableStorage;

namespace Edict.Core.TableStorage;

/// <summary>
/// Framework-internal factory that resolves a write store for a named table (ADR 0015).
/// The grain base calls this in <see cref="Orleans.Grain.OnActivateAsync"/> so each
/// concrete grain targets its own table without coupling to a specific provider.
/// </summary>
public interface IEdictTableStoreFactory
{
    Task<IEdictTableWriteStore<T>> CreateAsync<T>(
        string tableName,
        CancellationToken cancellationToken = default)
        where T : class, new();
}
