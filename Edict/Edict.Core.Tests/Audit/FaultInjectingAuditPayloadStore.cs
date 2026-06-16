using System.Collections.Concurrent;

using Edict.Contracts.Audit;
using Edict.Core.Audit;

namespace Edict.Core.Tests.Audit;

// A fault-injecting IEdictAuditPayloadStore for the drain-failure tests, kept
// separate from RecordingAuditPayloadStore so that store stays an honest
// always-succeeding oracle. ThrowOnNextPut faults exactly the next PutAsync and then
// clears, so a test can fail one drain and let the retry through. Put dedups on
// record id the way the substrate stores do, so a re-drain after a crash between put
// and ack writes the body again as a no-op; Get throws the same not-found signal.
// Implemented explicitly because the seam's payload type is internal.
public sealed class FaultInjectingAuditPayloadStore : IEdictAuditPayloadStore
{
    readonly ConcurrentDictionary<Guid, byte[]> _payloads = new();

    public bool ThrowOnNextPut { get; set; }

    public int Count => _payloads.Count;

    public bool Contains(Guid recordId) => _payloads.ContainsKey(recordId);

    Task IEdictAuditPayloadStore.PutAsync(IReadOnlyList<EdictAuditPayload> payloads, CancellationToken cancellationToken)
    {
        if (ThrowOnNextPut)
        {
            ThrowOnNextPut = false;
            throw new InvalidOperationException("Injected audit payload store fault.");
        }

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

        throw new EdictAuditPayloadNotFoundException(recordId, $"No audit payload for record '{recordId:N}'.");
    }
}
