using System.Collections.Concurrent;

using Edict.Contracts.ClaimCheck;

namespace Edict.Core.Tests.ClaimCheck;

/// <summary>
/// Test-double <see cref="IEdictClaimCheckStore"/> used by the in-memory
/// cluster fixture. The shipped <c>InMemoryClaimCheckStore</c> in
/// <c>Edict.Testing</c> is the customer-facing one; this is the lightweight
/// fake the framework's own provider-agnostic tests use so the Core test
/// assembly does not take a dependency on the test framework. Mirrors the
/// shipped store's contract: append-only, missing-blob throws
/// <see cref="KeyNotFoundException"/>.
/// </summary>
public sealed class InMemoryClaimCheckStore : IEdictClaimCheckStore
{
    readonly ConcurrentDictionary<string, byte[]> _blobs = new();

    /// <summary>
    /// Stages bytes under the supplied key so a subsequent grain-side fetch
    /// resolves them. Used by tests to set up a pointer-bearing envelope's
    /// inner-event payload without going through <see cref="PutAsync"/>.
    /// </summary>
    public void Seed(string key, byte[] payload) => _blobs[key] = payload;

    /// <summary>Removes the blob under <paramref name="key"/> to simulate a lifecycle reap.</summary>
    public void Reap(string key) => _blobs.TryRemove(key, out _);

    public Task<string> PutAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var key = $"edict-claim-check/{Guid.NewGuid():N}";
        _blobs[key] = payload.ToArray();
        return Task.FromResult(key);
    }

    public Task<ReadOnlyMemory<byte>> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (!_blobs.TryGetValue(key, out var bytes))
        {
            throw new KeyNotFoundException(
                $"Claim-check blob '{key}' was not found in the in-memory store.");
        }

        return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
    }
}
