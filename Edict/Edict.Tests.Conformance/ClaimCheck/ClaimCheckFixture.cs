namespace Edict.Tests.Conformance.ClaimCheck;

/// <summary>
/// Conformance fixture base for claim-check scenarios: the silo is wired with
/// a substrate-backed <c>IEdictClaimCheckStore</c> at a 1-byte threshold so
/// every raised event takes the pointer branch. Each provider's subclass owns
/// substrate bring-up plus a probe back to its underlying claim-check store
/// so the large-payload scenario can verify the body landed in durable
/// storage without touching a provider SDK.
/// </summary>
public abstract class ClaimCheckFixture : ConformanceFixture
{
    public abstract Task<bool> ClaimCheckBlobExistsAsync(string key);
}
