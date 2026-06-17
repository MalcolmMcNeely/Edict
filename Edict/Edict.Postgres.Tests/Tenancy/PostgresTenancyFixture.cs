using Edict.Contracts.Projections;
using Edict.Contracts.Tenancy;
using Edict.Tests.Conformance.Tenancy;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Edict.Postgres.Tests.Tenancy;

/// <summary>
/// A Postgres persistence-axis fixture with tenancy on: the same real Postgres grain
/// storage and table store behind a dumb <c>MemoryStreams</c> reference, plus
/// <c>AddEdictTenant</c> on the silo and client. Backs the headline read scenario —
/// one tenant lists its own employees while another's identical query is empty by
/// construction — on a real persistence backend.
/// </summary>
public sealed class PostgresTenancyFixture : PostgresPersistenceFixtureBase, IEdictTenancyConformanceFixture
{
    protected override bool EnableTenancy => true;

    // Tenancy fails closed at an origin send with no ambient tenant, and the base
    // bring-up issues a warm-up command, so seed a tenant before the cluster deploys.
    public override async Task InitializeAsync()
    {
        ConformanceTenantSource.Current = EdictTenantId.Of("warmup");
        await base.InitializeAsync();
    }

    public IEdictTenantScopedListProjectionReader<EmployeeDirectoryRow> EmployeeDirectoryReader =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictTenantScopedListProjectionReader<EmployeeDirectoryRow>>();
}

[CollectionDefinition(Name)]
public sealed class PostgresTenancyCollection : ICollectionFixture<PostgresTenancyFixture>
{
    public const string Name = "PostgresTenancy";
}
