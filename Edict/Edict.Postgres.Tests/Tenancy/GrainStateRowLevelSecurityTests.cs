using Edict.Contracts.Tenancy;
using Edict.Postgres.Persistence;
using Edict.Telemetry;
using Edict.Tests.Conformance.Tenancy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Orleans.Runtime;
using Orleans.Storage;

using Xunit;

namespace Edict.Postgres.Tests.Tenancy;

/// <summary>
/// The grain-state half of the Postgres second wall: the authoritative aggregate state
/// is keyed <c>{tenant}|{guid}</c>, so a row-security policy on <c>edict_grain_state</c>
/// confines a read to the ambient tenant's rows at the database layer. The same grain id
/// is read under two ambient walls; only the relay tenant the storage folds into the
/// per-transaction <c>SET LOCAL</c> changes, so the difference is the policy, not the
/// queried key. Proven through a restricted (non-superuser) role — the silo's own
/// superuser connection bypasses RLS.
/// </summary>
[Collection(PostgresTenancyCollection.Name)]
public sealed class GrainStateRowLevelSecurityTests(PostgresTenancyFixture fixture)
{
    const string ServiceId = "edict-rls-test";

    [Fact]
    public async Task RowLevelSecurity_DeniesACrossTenantGrainStateRead_EvenWhenTheKeyWouldAdmitIt()
    {
        var serializer = fixture.ClientSerializer;
        var superuserDataSource = fixture.DataSource;
        var services = new ServiceCollection().BuildServiceProvider();
        var grainId = GrainId.Create("Employee", $"globex|{Guid.NewGuid():N}");

        // Arrange: seed Globex's grain-state row through the superuser data source.
        var seedStorage = new EdictPostgresGrainStorage(
            superuserDataSource, ServiceId, serializer, services,
            NullLogger<EdictPostgresGrainStorage>.Instance, PostgresTransientRetry.Policy.Default);
        await seedStorage.WriteStateAsync("state", grainId,
            new TestGrainState<EmployeeState> { State = new EmployeeState { Department = "Engineering" } });

        // Arrange: a connection as a non-superuser role, the only context in which RLS bites.
        var roleName = $"edict_rls_{Guid.NewGuid():N}";
        await using var restrictedDataSource = await PostgresRestrictedRole.BuildAsync(
            superuserDataSource, roleName, password: "rls-probe", "edict_grain_state");
        var restrictedStorage = new EdictPostgresGrainStorage(
            restrictedDataSource, ServiceId, serializer, services,
            NullLogger<EdictPostgresGrainStorage>.Instance, PostgresTransientRetry.Policy.Default);

        try
        {
            // Act: read the identical grain id under two ambient walls.
            var deniedRead = new TestGrainState<EmployeeState>();
            TenantRelay.Seed(EdictTenantId.Of("acme"));
            await restrictedStorage.ReadStateAsync("state", grainId, deniedRead);

            var admittedRead = new TestGrainState<EmployeeState>();
            TenantRelay.Seed(EdictTenantId.Of("globex"));
            await restrictedStorage.ReadStateAsync("state", grainId, admittedRead);

            // Assert: Acme's ambient context cannot see Globex's row even though the grain id
            // matches; Globex's own context reads it back.
            Assert.False(deniedRead.RecordExists);
            Assert.True(admittedRead.RecordExists);
            Assert.Equal("Engineering", admittedRead.State.Department);
        }
        finally
        {
            TenantRelay.Seed(null);
        }
    }

    sealed class TestGrainState<T> : IGrainState<T>
        where T : new()
    {
        public T State { get; set; } = new();
        public string? ETag { get; set; }
        public bool RecordExists { get; set; }
    }
}
