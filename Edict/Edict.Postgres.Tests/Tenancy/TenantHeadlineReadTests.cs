using Edict.Contracts.Projections;
using Edict.Contracts.Tenancy;
using Edict.Tests.Conformance.Tenancy;

using Xunit;

namespace Edict.Postgres.Tests.Tenancy;

// The headline, on a real persistence backend: one tenant writes employees and lists
// its own; another tenant runs the identical query and gets nothing. The denial is
// structural — tenant B's read composes its own partition and there is simply nothing
// there — not a permission check applied after the fact.
[Collection(PostgresTenancyCollection.Name)]
public sealed class TenantHeadlineReadTests(PostgresTenancyFixture fixture)
{
    [Fact]
    public async Task TenantListsOwnEmployees_WhileAnotherTenantsIdenticalQueryIsEmpty()
    {
        var acme = EdictTenantId.Of("acme");
        var globex = EdictTenantId.Of("globex");
        var reader = fixture.EmployeeDirectoryReader;

        // Arrange + Act: act as Acme and add three employees. The bare send is the
        // ambient-resolver path EDICT024 names as its sanctioned suppression: the
        // registered AddEdictTenant resolver attributes the tenant at runtime.
        ConformanceTenantSource.Current = acme;
        for (var index = 0; index < 3; index++)
        {
#pragma warning disable EDICT024
            await fixture.Sender.SendAsync(new AddEmployeeCommand(new EmployeeId(Guid.NewGuid()), "Engineering"));
#pragma warning restore EDICT024
        }

        // Act: Acme lists its own directory.
        var acmeDirectory = await WaitForPartitionCountAsync(reader, expectedCount: 3);

        // Act: Globex runs the identical query under its own ambient tenant.
        ConformanceTenantSource.Current = globex;
        var globexDirectory = await reader.QueryMyPartitionAsync();

        // Assert: Acme sees its three; Globex sees none, by construction.
        Assert.Equal(3, acmeDirectory.Rows.Count);
        Assert.Empty(globexDirectory.Rows);
    }

    static async Task<EdictProjectionPartitionRead<EmployeeDirectoryRow>> WaitForPartitionCountAsync(
        IEdictTenantScopedListProjectionReader<EmployeeDirectoryRow> reader, int expectedCount)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        EdictProjectionPartitionRead<EmployeeDirectoryRow> read;
        do
        {
            read = await reader.QueryMyPartitionAsync();
            if (read.Rows.Count >= expectedCount)
            {
                return read;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        while (DateTimeOffset.UtcNow < deadline);

        return read;
    }
}
