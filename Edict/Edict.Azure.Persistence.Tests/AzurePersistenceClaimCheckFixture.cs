using Edict.Contracts.Tenancy;
using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Azure.Persistence.Tests;

/// <summary>
/// Persistence-axis fixture at a 1-byte claim-check threshold so every raised
/// event takes the pointer branch, exercising the receiver-unwrap → durable-row
/// path on the real Azure Blob claim-check store. Also forwards the
/// <see cref="IClaimCheckStoreFixture"/> seam so the claim-check key-contract
/// scenarios round-trip directly against the real store (put/get/miss by
/// <c>EventId</c>) without standing up a publish path.
/// </summary>
public sealed class AzurePersistenceClaimCheckFixture : AzurePersistenceFixtureBase, IClaimCheckStoreFixture
{
    protected override int? ClaimCheckThresholdBytes => 1;

    public Task PutClaimCheckAsync(EdictTenantId? tenant, Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        ClaimCheckStore.PutAsync(tenant, eventId, payload, cancellationToken);

    public Task<ReadOnlyMemory<byte>> GetClaimCheckAsync(EdictTenantId? tenant, Guid eventId, CancellationToken cancellationToken) =>
        ClaimCheckStore.GetAsync(tenant, eventId, cancellationToken);
}

[CollectionDefinition(Name)]
public sealed class AzurePersistenceClaimCheckCollection
    : ICollectionFixture<AzurePersistenceClaimCheckFixture>
{
    public const string Name = "AzurePersistenceClaimCheck";
}
