using Edict.Contracts.ClaimCheck;

namespace Edict.Tests.Conformance.ClaimCheck;

/// <summary>
/// In-memory binding fixture for the claim-check key-contract battery. No real
/// substrate is needed, so it forwards to a
/// <see cref="ConformanceInMemoryClaimCheckStore"/>; running the same scenarios
/// here proves the in-process double satisfies the observable key contract
/// end-to-end, identically to the production stores.
/// </summary>
public sealed class InMemoryClaimCheckStoreFixture : IClaimCheckStoreFixture
{
    readonly IEdictClaimCheckStore _store = new ConformanceInMemoryClaimCheckStore();

    public Task PutClaimCheckAsync(Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        _store.PutAsync(eventId, payload, cancellationToken);

    public Task<ReadOnlyMemory<byte>> GetClaimCheckAsync(Guid eventId, CancellationToken cancellationToken) =>
        _store.GetAsync(eventId, cancellationToken);
}
