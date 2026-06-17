using Edict.Contracts.Configuration;
using Edict.Contracts.Projections;
using Edict.Contracts.Tenancy;
using Edict.Tests.Conformance.ClaimCheck;
using Edict.Tests.Conformance.Tenancy;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Edict.Azure.Persistence.Tests;

/// <summary>
/// The Azure persistence-axis fixture backing the stolen-key adversarial battery:
/// tenancy on (the ambient-scoped reader and the isolation call filter), the real
/// publish executor swapped for <c>ControllableOutboxExecutor</c> at
/// <c>OutboxMaxAttempts</c> = 2 so a poisoned tenant-scoped command promotes to a
/// dead-letter row, and the real Azure Blob claim-check store forwarded through the
/// <see cref="IClaimCheckStoreFixture"/> seam. One silo serves all four doors the
/// battery hammers, on the real Azure store where keying, the call filter, and
/// conformance are the sole isolation controls (Azure Table has no Row-Level
/// Security equivalent).
/// </summary>
public sealed class AzureStolenRouteKeyFixture : AzurePersistenceFixtureBase, IEdictTenancyConformanceFixture, IClaimCheckStoreFixture
{
    protected override bool EnableTenancy => true;

    protected override bool ReplacePublishExecutorWithControllable => true;

    protected override Action<EdictOptions>? ConfigureOptions => options =>
    {
        options.OutboxMaxAttempts = 2;
        options.OutboxBaseDelay = TimeSpan.FromMilliseconds(200);
        options.OutboxJitterFraction = 0;
    };

    // Tenancy fails closed at an origin send with no ambient tenant, and the base
    // bring-up issues a warm-up command, so seed a tenant before the cluster deploys.
    public override async Task InitializeAsync()
    {
        ConformanceTenantSource.Current = EdictTenantId.Of("warmup");
        await base.InitializeAsync();
    }

    public IEdictTenantScopedListProjectionReader<EmployeeDirectoryRow> EmployeeDirectoryReader =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictTenantScopedListProjectionReader<EmployeeDirectoryRow>>();

    public Task PutClaimCheckAsync(EdictTenantId? tenant, Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        ClaimCheckStore.PutAsync(tenant, eventId, payload, cancellationToken);

    public Task<ReadOnlyMemory<byte>> GetClaimCheckAsync(EdictTenantId? tenant, Guid eventId, CancellationToken cancellationToken) =>
        ClaimCheckStore.GetAsync(tenant, eventId, cancellationToken);
}

[CollectionDefinition(Name)]
public sealed class AzureStolenRouteKeyCollection : ICollectionFixture<AzureStolenRouteKeyFixture>
{
    public const string Name = "AzureStolenRouteKey";
}
