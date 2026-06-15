using System.Collections.Concurrent;

using Edict.Contracts.Audit;
using Edict.Core.Audit;

namespace Edict.Testing.Internal;

// In-memory IEdictAuditPayloadStore shipped with the Test Framework so a consumer
// asserts a captured body without a container. Put dedups on record id the way the
// substrate stores do, so a re-drain after a crash between put and ack is a no-op;
// Get throws the same not-found signal the substrate stores raise. Implemented
// explicitly because the seam's payload type is internal — the consumer reaches the
// body back through IEdictAuditRepository.GetPayloadAsync anyway.
sealed class InMemoryAuditPayloadStore : IEdictAuditPayloadStore
{
    readonly ConcurrentDictionary<Guid, byte[]> _payloads = new();

    Task IEdictAuditPayloadStore.PutAsync(IReadOnlyList<EdictAuditPayload> payloads, CancellationToken cancellationToken)
    {
        foreach (var payload in payloads)
        {
            _payloads.TryAdd(payload.RecordId, payload.Body.ToArray());
        }
        return Task.CompletedTask;
    }

    Task<ReadOnlyMemory<byte>> IEdictAuditPayloadStore.GetAsync(Guid recordId, CancellationToken cancellationToken)
    {
        if (_payloads.TryGetValue(recordId, out var body))
        {
            return Task.FromResult<ReadOnlyMemory<byte>>(body);
        }

        throw new EdictAuditPayloadNotFoundException(recordId, $"No audit payload for record '{recordId:N}' in the in-memory store.");
    }
}
