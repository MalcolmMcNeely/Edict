namespace Edict.Tests.Conformance.ClaimCheck;

/// <summary>
/// Narrow fixture seam for the claim-check key-contract battery. It forwards to
/// the substrate-backed <c>IEdictClaimCheckStore</c> the binding wires, exposing
/// only the two operations the scenarios exercise so a binding does not have to
/// stand up a full TestCluster. The forwarding signatures use public types only
/// (the store seam itself is framework-internal), letting the same scenarios run
/// against every substrate — real or in-process — without widening that seam.
/// </summary>
public interface IClaimCheckStoreFixture
{
    Task PutClaimCheckAsync(Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

    Task<ReadOnlyMemory<byte>> GetClaimCheckAsync(Guid eventId, CancellationToken cancellationToken);
}
