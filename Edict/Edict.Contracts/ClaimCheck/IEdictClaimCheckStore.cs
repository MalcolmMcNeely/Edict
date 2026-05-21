namespace Edict.Contracts.ClaimCheck;

/// <summary>
/// Framework-internal seam over the append-only blob store that backs the
/// claim-check escape hatch for oversized events. The surface is
/// deliberately minimal — only <see cref="PutAsync"/> and
/// <see cref="GetAsync"/>:
/// <list type="bullet">
///   <item>No <c>DeleteAsync</c>: the store is append-only by design;
///         retention is owned by the storage account's lifecycle policy so
///         a framework bug or a configuration mistake cannot erase
///         forensic evidence.</item>
///   <item>No <c>ExistsAsync</c>: would invite TOCTOU races between the
///         existence check and the subsequent fetch. Missing blobs surface
///         as a <c>GetAsync</c> exception which the receiver pipeline
///         funnels into the dead-letter promotion path.</item>
/// </list>
/// Key generation is the store's responsibility — neither the publisher
/// nor the receiver attempts to derive or interpret the key string.
/// </summary>
public interface IEdictClaimCheckStore
{
    /// <summary>
    /// Persist <paramref name="payload"/> and return the store-generated
    /// key that subsequent <see cref="GetAsync"/> calls will use to fetch
    /// it. The store is append-only — once written, a blob is never
    /// rewritten or deleted by the framework.
    /// </summary>
    Task<string> PutAsync(ReadOnlyMemory<byte> payload, CancellationToken ct);

    /// <summary>
    /// Fetch the payload previously written under <paramref name="key"/>.
    /// A missing blob throws a provider-defined exception which the
    /// receiver pipeline treats as a transient delivery failure (retry +
    /// dead-letter on exhaustion).
    /// </summary>
    Task<ReadOnlyMemory<byte>> GetAsync(string key, CancellationToken ct);
}
