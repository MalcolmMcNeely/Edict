using Edict.Contracts.Configuration;
using Edict.Contracts.Tenancy;
using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Azure.Persistence.Tests;

/// <summary>
/// Persistence-axis fixture that wraps the real Azure Blob claim-check store with
/// the controllable fault decorator so the transient-recovery scenario can hold a
/// receiver-side fetch down for a count-addressed number of attempts, then heal.
/// The outbox cap sits comfortably above those faults so the entry recovers before
/// it would dead-letter, and the minimal backoff lets reminder-drain ticks converge
/// it with no wall-clock gate. Forwards the <see cref="IClaimCheckStoreFixture"/>
/// seam (against the same store) so the scenario can park the body the fetch
/// eventually returns.
/// </summary>
public sealed class AzurePersistenceClaimCheckTransientRecoveryFixture : AzurePersistenceFixtureBase, IClaimCheckStoreFixture
{
    protected override bool WrapClaimCheckStoreWithControllable => true;

    protected override Action<EdictOptions>? ConfigureOptions => options =>
    {
        options.OutboxMaxAttempts = 5;
        options.OutboxBaseDelay = TimeSpan.FromMilliseconds(1);
        options.OutboxJitterFraction = 0;
        options.OutboxDrainReminderPeriod = TimeSpan.FromMinutes(2);
    };

    public Task PutClaimCheckAsync(EdictTenantId? tenant, Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        ClaimCheckStore.PutAsync(tenant, eventId, payload, cancellationToken);

    public Task<ReadOnlyMemory<byte>> GetClaimCheckAsync(EdictTenantId? tenant, Guid eventId, CancellationToken cancellationToken) =>
        ClaimCheckStore.GetAsync(tenant, eventId, cancellationToken);
}

[CollectionDefinition(Name)]
public sealed class AzurePersistenceClaimCheckTransientRecoveryCollection
    : ICollectionFixture<AzurePersistenceClaimCheckTransientRecoveryFixture>
{
    public const string Name = "AzurePersistenceClaimCheckTransientRecovery";
}
