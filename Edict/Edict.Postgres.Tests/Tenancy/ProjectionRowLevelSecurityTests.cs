using Edict.Contracts.Tenancy;
using Edict.Postgres.TableStorage;
using Edict.Telemetry;
using Edict.Tests.Conformance.Tenancy;

using Xunit;

namespace Edict.Postgres.Tests.Tenancy;

/// <summary>
/// The Postgres-only second wall: even granting that the C# key would admit the read,
/// the database denies a cross-tenant projection query. The queried partition key is
/// held identical across both reads — only the ambient tenant the store folds into the
/// per-transaction <c>SET LOCAL</c> changes — so the difference in outcome is the
/// Row-Level Security policy, not the key. Proven through a restricted (non-superuser)
/// role, the only context in which Postgres enforces RLS.
/// </summary>
[Collection(PostgresTenancyCollection.Name)]
public sealed class ProjectionRowLevelSecurityTests(PostgresTenancyFixture fixture)
{
    [Fact]
    public async Task RowLevelSecurity_DeniesACrossTenantPartitionRead_EvenWhenTheKeyWouldAdmitIt()
    {
        var table = $"rls_projection_{Guid.NewGuid():N}";
        var serializer = fixture.ClientSerializer;
        var superuserDataSource = fixture.DataSource;

        // Arrange: create the projection table (which arms the RLS policy) and seed a
        // Globex row through the superuser data source.
        await PostgresTableSchema.EnsureProjectionTableAsync(superuserDataSource, table, CancellationToken.None);
        var seedStore = new PostgresTableWriteStore<EmployeeDirectoryRow>(superuserDataSource, table, serializer);
        await seedStore.UpsertAsync("globex", "employee-1", new EmployeeDirectoryRow { Department = "Engineering" });

        // Arrange: a connection as a non-superuser role, the only context in which RLS is
        // enforced — the superuser the silo connects as bypasses the policy entirely.
        var roleName = $"edict_rls_{Guid.NewGuid():N}";
        await using var restrictedDataSource = await PostgresRestrictedRole.BuildAsync(
            superuserDataSource, roleName, password: "rls-probe", table);
        var restrictedStore = new PostgresTableWriteStore<EmployeeDirectoryRow>(restrictedDataSource, table, serializer);

        try
        {
            // Act: read the identical Globex partition under two ambient walls.
            TenantRelay.Seed(EdictTenantId.Of("acme"));
            var deniedRead = await restrictedStore.QueryPartitionAsync("globex");

            TenantRelay.Seed(EdictTenantId.Of("globex"));
            var admittedRead = await restrictedStore.QueryPartitionAsync("globex");

            // Assert: Acme's ambient context sees nothing in Globex's partition even though
            // the partition key matches; Globex's own context sees its row.
            Assert.Empty(deniedRead);
            Assert.Single(admittedRead);
        }
        finally
        {
            TenantRelay.Seed(null);
        }
    }
}
