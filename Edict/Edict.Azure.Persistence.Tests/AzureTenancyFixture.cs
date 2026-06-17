using Edict.Contracts.Projections;
using Edict.Contracts.Tenancy;
using Edict.Tests.Conformance.Tenancy;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Edict.Azure.Persistence.Tests;

/// <summary>
/// An Azure persistence-axis fixture with tenancy on: the same real Azure Blob
/// grain storage and Azure Table store behind a dumb <c>MemoryStreams</c>
/// reference, plus <c>AddEdictTenant</c> on the silo and client. Backs the
/// tenant-isolation scenarios — the ambient-scoped List read and the per-tenant
/// row partition — on real Azure Table, where keying, the call filter, and
/// conformance are the sole isolation controls (Azure Table has no Row-Level
/// Security equivalent).
/// </summary>
public sealed class AzureTenancyFixture : AzurePersistenceFixtureBase, IEdictTenancyConformanceFixture
{
    protected override bool EnableTenancy => true;

    // Tenancy fails closed at an origin send with no ambient tenant, so seed one
    // before the cluster deploys rather than leave the static at its null default.
    public override async Task InitializeAsync()
    {
        ConformanceTenantSource.Current = EdictTenantId.Of("warmup");
        await base.InitializeAsync();
    }

    public IEdictTenantScopedListProjectionReader<EmployeeDirectoryRow> EmployeeDirectoryReader =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictTenantScopedListProjectionReader<EmployeeDirectoryRow>>();
}

[CollectionDefinition(Name)]
public sealed class AzureTenancyCollection : ICollectionFixture<AzureTenancyFixture>
{
    public const string Name = "AzureTenancy";
}
