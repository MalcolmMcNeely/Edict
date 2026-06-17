using Edict.Contracts.Tenancy;
using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Postgres.Tests;

/// <summary>
/// Persistence-axis fixture for the claim-check threshold-boundary scenario: the
/// silo computes its threshold as the serialized size of a reference-length probe
/// event, and runs on the frozen virtual clock so a raised event's
/// <c>OccurredAt</c> — the only variable-width field — is pinned to the epoch the
/// threshold was computed against. Forwards the <see cref="IClaimCheckStoreFixture"/>
/// seam so the scenario can ask the real Postgres store whether a body was parked
/// (pointer) or not (inline).
/// </summary>
public sealed class PostgresPersistenceClaimCheckThresholdFixture : PostgresPersistenceFixtureBase, IClaimCheckStoreFixture
{
    protected override bool ComputeThresholdFromReferenceProbe => true;

    protected override bool UsesVirtualClock => true;

    public Task PutClaimCheckAsync(EdictTenantId? tenant, Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        ClaimCheckStore.PutAsync(tenant, eventId, payload, cancellationToken);

    public Task<ReadOnlyMemory<byte>> GetClaimCheckAsync(EdictTenantId? tenant, Guid eventId, CancellationToken cancellationToken) =>
        ClaimCheckStore.GetAsync(tenant, eventId, cancellationToken);
}

[CollectionDefinition(Name)]
public sealed class PostgresPersistenceClaimCheckThresholdCollection
    : ICollectionFixture<PostgresPersistenceClaimCheckThresholdFixture>
{
    public const string Name = "PostgresPersistenceClaimCheckThreshold";
}
