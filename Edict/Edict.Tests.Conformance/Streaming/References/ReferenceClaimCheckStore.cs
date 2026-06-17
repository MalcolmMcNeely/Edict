using System.Collections.Concurrent;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;
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
    readonly ConcurrentDictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

    // The streaming scenarios spill public events, so the existence probe addresses
    // the bare (null-tenant) key the same fold produced at put time.
    public bool Exists(Guid eventId) => _blobs.ContainsKey(KeyFor(tenant: null, eventId));

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
                $"Claim-check payload not found for event '{eventId:N}' in the reference store.");
        }

        return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
    }

    static string KeyFor(EdictTenantId? tenant, Guid eventId) =>
        EdictKeyComposer.Compose(tenant, eventId.ToString("N"));
}
