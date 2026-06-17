using Edict.Contracts.TableStorage;
using Edict.Contracts.Tenancy;
using Edict.Tests.Conformance.Tenancy;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Grain state, on a real persistence backend: the same route key under two
/// tenants addresses two distinct grains, so the state each derives lands in its
/// own tenant partition and can never share a row. Two tenants add an employee at
/// the <em>identical</em> <c>EmployeeId</c> with different departments; the durable
/// projection row each grain writes is recovered store-direct under its own tenant
/// partition, proving the composed <c>{tenant}|{guid}</c> key segregates the
/// persisted state physically — the isolation that placement on Azure Table (which
/// has no Row-Level Security) rests on, proven on the real store rather than at the
/// key-composition seam.
/// </summary>
public abstract class TenantScopedGrainStateIsolationScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture, IEdictTenancyConformanceFixture
{
    readonly TFixture _fixture;

    protected TenantScopedGrainStateIsolationScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SameEmployeeIdUnderTwoTenants_ShouldPersistIsolatedRowsInEachTenantPartition()
    {
        var acme = EdictTenantId.Of("acme");
        var globex = EdictTenantId.Of("globex");

        // Arrange + Act: the same EmployeeId, written under two walls with distinct
        // departments. The route key is shared; the tenant is the only difference.
        var employeeId = new EmployeeId(Guid.NewGuid());
        var rowKey = employeeId.Value.ToString("N");

        ConformanceTenantSource.Current = acme;
#pragma warning disable EDICT024
        await _fixture.Sender.SendAsync(new AddEmployeeCommand(employeeId, "Engineering"));

        ConformanceTenantSource.Current = globex;
        await _fixture.Sender.SendAsync(new AddEmployeeCommand(employeeId, "Sales"));
#pragma warning restore EDICT024

        var store = _fixture.GetTableStore<EmployeeDirectoryRow>(EmployeeDirectoryProjectionBuilder.Table);

        // Act: each tenant's row is recovered store-direct under its own partition.
        var acmeRow = await WaitForRowAsync(store, partitionKey: acme.Value, rowKey);
        var globexRow = await WaitForRowAsync(store, partitionKey: globex.Value, rowKey);

        // Assert: the shared route key produced two distinct rows, one per wall.
        Assert.NotNull(acmeRow);
        Assert.NotNull(globexRow);
        Assert.Equal("Engineering", acmeRow.Department);
        Assert.Equal("Sales", globexRow.Department);
    }

    static async Task<EmployeeDirectoryRow?> WaitForRowAsync(
        IEdictTableWriteStore<EmployeeDirectoryRow> store, string partitionKey, string rowKey)
    {
        EmployeeDirectoryRow? row = null;
        await ConformanceWaiters.WaitUntilAsync(async () => (row = await store.GetAsync(partitionKey, rowKey)) is not null);
        return row;
    }
}
