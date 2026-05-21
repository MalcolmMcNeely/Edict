using Edict.Contracts.TableStorage;

namespace Edict.Core.TableStorage;

/// <summary>
/// Framework-internal factory that resolves a write store for a named table.
/// The grain base calls this in <see cref="Orleans.Grain.OnActivateAsync"/> so each
/// concrete grain targets its own table without coupling to a specific provider.
/// </summary>
public interface IEdictTableStoreFactory
{
    Task<IEdictTableWriteStore<T>> CreateAsync<T>(string tableName, CancellationToken cancellationToken = default) where T : class, new();

    /// <summary>
    /// Non-generic upsert seam for the Outbox <c>UpsertRow</c> drain:
    /// the drained effect carries the row as a deserialized <see cref="object"/>
    /// (its concrete CLR type is reconstructed from the entry), so the executor
    /// cannot call the generic <see cref="CreateAsync{T}"/>. Idempotent by
    /// <paramref name="partitionKey"/>/<paramref name="rowKey"/> — a full-row
    /// replace, so at-least-once effect redelivery does not double-apply.
    /// </summary>
    Task UpsertRowAsync(string tableName, string partitionKey, string rowKey, object row, CancellationToken cancellationToken = default);
}
