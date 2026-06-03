using System.Collections.Concurrent;

using Edict.Contracts.ClaimCheck;
using Edict.Core.DeadLetter;

namespace Edict.Tests.Conformance.ClaimCheck;

/// <summary>
/// In-memory <see cref="IEdictClaimCheckStore"/> double the conformance battery
/// runs as its fourth "substrate". It exists here, rather than borrowing the
/// fakes in <c>Edict.Core.Tests</c> or the shipped store in <c>Edict.Testing</c>,
/// because the conformance assembly must not depend on a unit-test or
/// consumer-only project. Keyed by the event's <c>EventId</c> and, on a miss,
/// raising the same <see cref="EdictClaimCheckFetchException"/> the production
/// stores raise — so an in-process double exhibits the production dead-letter
/// classification (<c>Substrate</c>) rather than a divergent one.
/// </summary>
public sealed class ConformanceInMemoryClaimCheckStore : IEdictClaimCheckStore
{
    readonly ConcurrentDictionary<Guid, byte[]> _blobs = new();

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
