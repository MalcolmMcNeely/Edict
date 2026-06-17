using System.Collections.Concurrent;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;
using Edict.Core.DeadLetter;

namespace Edict.Testing.Internal;

/// <summary>
/// In-memory <see cref="IEdictClaimCheckStore"/> shipped with the test
/// framework. Append-only — no <c>DeleteAsync</c>, in keeping with the seam
/// contract (the append-only model). Keyed by the tenant-folded <c>EventId</c>
/// and, on a miss, throws the same <see cref="EdictClaimCheckFetchException"/> the
/// production stores raise — so an in-process test reproduces the production
/// dead-letter classification (<c>Substrate</c>/<c>BlobMissing</c>) and the
/// tenant isolation the real stores enforce.
/// </summary>
sealed class InMemoryClaimCheckStore : IEdictClaimCheckStore
{
    readonly ConcurrentDictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

    public Task PutAsync(EdictTenantId? tenant, Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        _blobs[EdictKeyComposer.Compose(tenant, eventId.ToString("N"))] = payload.ToArray();
        return Task.CompletedTask;
    }

    public Task<ReadOnlyMemory<byte>> GetAsync(EdictTenantId? tenant, Guid eventId, CancellationToken cancellationToken)
    {
        if (!_blobs.TryGetValue(EdictKeyComposer.Compose(tenant, eventId.ToString("N")), out var bytes))
        {
            throw new EdictClaimCheckFetchException(
                eventId,
                $"Claim-check payload not found for event '{eventId:N}' in the in-memory store.");
        }

        return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
    }
}
