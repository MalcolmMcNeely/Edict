using System.Collections.Concurrent;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;
using Edict.Core.DeadLetter;

namespace Edict.Core.Tests.ClaimCheck;

/// <summary>
/// Test-double <see cref="IEdictClaimCheckStore"/> used by the in-memory
/// cluster fixture. The shipped <c>InMemoryClaimCheckStore</c> in
/// <c>Edict.Testing</c> is the customer-facing one; this is the lightweight
/// fake the framework's own provider-agnostic tests use so the Core test
/// assembly does not take a dependency on the test framework. Mirrors the
/// shipped store's contract: keyed by the tenant-folded <c>EventId</c>,
/// append-only, missing-blob throws <see cref="EdictClaimCheckFetchException"/>
/// (the same type the production stores raise) so the observable dead-letter
/// classification matches production.
/// </summary>
public sealed class InMemoryClaimCheckStore : IEdictClaimCheckStore
{
    readonly ConcurrentDictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

    /// <summary>
    /// Stages bytes under the supplied <paramref name="eventId"/> so a
    /// subsequent grain-side fetch resolves them. Used by tests to set up a
    /// pointer-bearing envelope's inner-event payload without going through
    /// <see cref="PutAsync"/>.
    /// </summary>
    public void Seed(EdictTenantId? tenant, Guid eventId, byte[] payload) => _blobs[KeyFor(tenant, eventId)] = payload;

    /// <summary>Removes the blob under <paramref name="eventId"/> to simulate a lifecycle reap.</summary>
    public void Reap(EdictTenantId? tenant, Guid eventId) => _blobs.TryRemove(KeyFor(tenant, eventId), out _);

    public Task PutAsync(EdictTenantId? tenant, Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        _blobs[KeyFor(tenant, eventId)] = payload.ToArray();
        return Task.CompletedTask;
    }

    public Task<ReadOnlyMemory<byte>> GetAsync(EdictTenantId? tenant, Guid eventId, CancellationToken cancellationToken)
    {
        if (!_blobs.TryGetValue(KeyFor(tenant, eventId), out var bytes))
        {
            throw new EdictClaimCheckFetchException(
                eventId,
                $"Claim-check payload not found for event '{eventId:N}' in the in-memory store.");
        }

        return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
    }

    static string KeyFor(EdictTenantId? tenant, Guid eventId) =>
        EdictKeyComposer.Compose(tenant, eventId.ToString("N"));
}
