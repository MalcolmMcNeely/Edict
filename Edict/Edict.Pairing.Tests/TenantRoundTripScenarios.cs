using Edict.Contracts.Projections;
using Edict.Contracts.Tenancy;
using Edict.Tests.Conformance.Tenancy;

using Xunit;

namespace Edict.Pairing.Tests;

/// <summary>
/// Bucket-4 tenant interaction: a tenant-scoped command folded into a
/// <c>{tenant}|{guid}</c> key routed over a <strong>real</strong> stream provider
/// into a tenant-partitioned projection on a <strong>real</strong> persistence
/// provider, read back through the ambient-scoped reader. One tenant sends and lists
/// its own employees; another tenant's identical query is empty by construction.
/// Neither axis battery shows this conjunction: the persistence battery's stream is a
/// dumb deliver-once reference that cannot carry the composed key over a real hop,
/// and the streaming battery's store is a reference that never partitions a real
/// table by tenant. Only both real backends at once prove the wall holds end to end.
/// </summary>
public abstract class TenantRoundTripScenarios<TFixture>
    where TFixture : PairingFixture
{
    readonly TFixture _fixture;

    protected TenantRoundTripScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TenantScopedCommand_RoundTripsAcrossBothRealBackends_IntoItsOwnWall()
    {
        var acme = EdictTenantId.Of("acme");
        var globex = EdictTenantId.Of("globex");
        var reader = _fixture.EmployeeDirectoryReader;

        // Arrange + Act — act as Acme and add three employees. Each command folds the
        // Acme wall into the grain and stream keys, rides the real stream hop, and
        // lands in the Acme partition of the real projection table.
        _fixture.TenantAmbient.Current = acme;
        for (var index = 0; index < 3; index++)
        {
#pragma warning disable EDICT024
            await _fixture.Sender.SendAsync(new AddEmployeeCommand(new EmployeeId(Guid.NewGuid()), "Engineering"));
#pragma warning restore EDICT024
        }

        var acmeDirectory = await WaitForPartitionCountAsync(reader, expectedCount: 3);

        // Act — Globex runs the identical query under its own ambient wall.
        _fixture.TenantAmbient.Current = globex;
        var globexDirectory = await reader.QueryMyPartitionAsync();

        // Assert — Acme's three round-tripped into its wall; Globex sees none.
        Assert.Equal(3, acmeDirectory.Rows.Count);
        Assert.Empty(globexDirectory.Rows);
    }

    static async Task<EdictProjectionPartitionRead<EmployeeDirectoryRow>> WaitForPartitionCountAsync(
        IEdictTenantScopedListProjectionReader<EmployeeDirectoryRow> reader, int expectedCount)
    {
        EdictProjectionPartitionRead<EmployeeDirectoryRow> read = default;
        await PairingWaiters.WaitUntilAsync(async () =>
        {
            read = await reader.QueryMyPartitionAsync();
            return read.Rows.Count >= expectedCount;
        });
        return read;
    }
}
