using System.Collections.Concurrent;

using Edict.Contracts.ClaimCheck;
using Edict.Core.DeadLetter;

namespace Edict.Tests.Conformance.Streaming.References;

/// <summary>
/// In-memory <see cref="IEdictClaimCheckStore"/> the streaming battery wires as
/// its reference persistence: the streaming axis stands up a real broker, so the
/// claim-check store only has to honour the round-trip contract (put/get by
/// <c>EventId</c>, miss throws <see cref="EdictClaimCheckFetchException"/> — the
/// same type the production stores raise), never a real fault mode. It is never
/// asserted upon for any persistence property; the streaming scenarios assert
/// only that the body landed (via <see cref="Exists"/>) or unwrapped, which is a
/// streaming-publish/receiver property. Pulls no provider SDK so the conformance
/// harness's SDK-purity guard stays green.
/// </summary>
public sealed class ReferenceClaimCheckStore : IEdictClaimCheckStore
{
    readonly ConcurrentDictionary<Guid, byte[]> _blobs = new();

    public bool Exists(Guid eventId) => _blobs.ContainsKey(eventId);

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
                $"Claim-check payload not found for event '{eventId:N}' in the reference store.");
        }

        return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
    }
}
