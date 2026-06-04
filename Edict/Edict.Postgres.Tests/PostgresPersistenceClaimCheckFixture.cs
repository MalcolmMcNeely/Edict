using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Postgres.Tests;

/// <summary>
/// Persistence-axis fixture at a 1-byte claim-check threshold so every raised
/// event takes the pointer branch, exercising the receiver-unwrap → durable-row
/// path on the real Postgres claim-check store. Also forwards the
/// <see cref="IClaimCheckStoreFixture"/> seam so the claim-check key-contract
/// scenarios round-trip directly against the real store (put/get/miss by
/// <c>EventId</c>) without standing up a publish path.
/// </summary>
public sealed class PostgresPersistenceClaimCheckFixture : PostgresPersistenceFixtureBase, IClaimCheckStoreFixture
{
    protected override int? ClaimCheckThresholdBytes => 1;

    public Task PutClaimCheckAsync(Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        ClaimCheckStore.PutAsync(eventId, payload, cancellationToken);

    public Task<ReadOnlyMemory<byte>> GetClaimCheckAsync(Guid eventId, CancellationToken cancellationToken) =>
        ClaimCheckStore.GetAsync(eventId, cancellationToken);
}

[CollectionDefinition(Name)]
public sealed class PostgresPersistenceClaimCheckCollection
    : ICollectionFixture<PostgresPersistenceClaimCheckFixture>
{
    public const string Name = "PostgresPersistenceClaimCheck";
}
