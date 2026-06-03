using System.Collections.Concurrent;

using Edict.Contracts.ClaimCheck;
using Edict.Core.DeadLetter;

namespace Edict.Core.Tests.ClaimCheck;

/// <summary>
/// Test-double <see cref="IEdictClaimCheckStore"/> used by the in-memory
/// cluster fixture. The shipped <c>InMemoryClaimCheckStore</c> in
/// <c>Edict.Testing</c> is the customer-facing one; this is the lightweight
/// fake the framework's own provider-agnostic tests use so the Core test
/// assembly does not take a dependency on the test framework. Mirrors the
/// shipped store's contract: keyed by the event's <c>EventId</c>, append-only,
/// missing-blob throws <see cref="EdictClaimCheckFetchException"/> (the same
/// type the production stores raise) so the observable dead-letter
/// classification matches production.
/// </summary>
public sealed class InMemoryClaimCheckStore : IEdictClaimCheckStore
{
    readonly ConcurrentDictionary<Guid, byte[]> _blobs = new();

    /// <summary>
    /// Stages bytes under the supplied <paramref name="eventId"/> so a
    /// subsequent grain-side fetch resolves them. Used by tests to set up a
    /// pointer-bearing envelope's inner-event payload without going through
    /// <see cref="PutAsync"/>.
    /// </summary>
    public void Seed(Guid eventId, byte[] payload) => _blobs[eventId] = payload;

    /// <summary>Removes the blob under <paramref name="eventId"/> to simulate a lifecycle reap.</summary>
    public void Reap(Guid eventId) => _blobs.TryRemove(eventId, out _);

    public Task PutAsync(Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        _blobs[eventId] = payload.ToArray();
        return Task.CompletedTask;
    }

    public Task<ReadOnlyMemory<byte>> GetAsync(Guid eventId, CancellationToken cancellationToken)
    {
        if (!_blobs.TryGetValue(eventId, out var bytes))
        {
            throw new EdictClaimCheckFetchException(
                eventId,
                $"Claim-check payload not found for event '{eventId:N}' in the in-memory store.");
        }

        return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
    }
}
