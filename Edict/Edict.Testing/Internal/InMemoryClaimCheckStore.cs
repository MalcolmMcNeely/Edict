using System.Collections.Concurrent;

using Edict.Contracts.ClaimCheck;

namespace Edict.Testing.Internal;

/// <summary>
/// In-memory <see cref="IEdictClaimCheckStore"/> shipped with the test
/// framework. Append-only — no <c>DeleteAsync</c>, in keeping with the seam
/// contract (the append-only model).
/// </summary>
sealed class InMemoryClaimCheckStore : IEdictClaimCheckStore
{
    readonly ConcurrentDictionary<string, byte[]> _blobs = new();

    public Task<string> PutAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var key = $"edict-claim-check/{Guid.NewGuid():N}";
        _blobs[key] = payload.ToArray();
        return Task.FromResult(key);
    }

    public Task<ReadOnlyMemory<byte>> GetAsync(string key, CancellationToken ct)
    {
        if (!_blobs.TryGetValue(key, out var bytes))
        {
            throw new KeyNotFoundException(
                $"Claim-check blob '{key}' was not found in the in-memory store.");
        }

        return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
    }
}
