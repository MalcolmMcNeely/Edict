using Azure.Data.Tables;

using Edict.Azure.Persistence.TableStorage;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;
using Edict.Core.DeadLetter;
using Edict.Tests.Conformance;

using Xunit;

namespace Edict.Azure.Persistence.Tests.TableStorage;

// Seam test: the Azure Table store round-trips a dead-letter row whose Tenant tag
// is set. Dead-letter stays operator-scoped but carries the source aggregate's wall
// (ADR-0067), and that wall is an EdictTenantId — not one of Azure Tables' native
// column types. The mapper must fold it to its string form on write and recover it
// on read; a row written with a null tenant (every public-aggregate failure) already
// stored fine, so the tag is the one column the operator-scoped path never exercised.
public sealed class AzureTableDeadLetterTenantRoundTripTests
{
    static async Task<AzureTableWriteStore<EdictDeadLetterEntry>> CreateStoreAsync()
    {
        var connectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        var tableServiceClient = new TableServiceClient(connectionString);
        var tableName = $"deadletter{Guid.NewGuid():N}";
        var tableClient = tableServiceClient.GetTableClient(tableName);
        await tableClient.CreateIfNotExistsAsync();
        return new AzureTableWriteStore<EdictDeadLetterEntry>(tableClient);
    }

    [Fact]
    public async Task UpsertAndQuery_RoundTripsTheTenantTagOnADeadLetterRow()
    {
        // Arrange
        var store = await CreateStoreAsync();
        var tenant = EdictTenantId.Of("acme");
        var entryId = Guid.NewGuid();
        var sourceGrainKey = EdictKeyComposer.Compose(tenant, entryId.ToString("N"));
        var rowKey = entryId.ToString("N");
        var row = new EdictDeadLetterEntry
        {
            EntryId = entryId,
            Kind = "PublishEvent",
            SourceGrainKey = sourceGrainKey,
            SourceGrainType = "EmployeeCommandHandler",
            Tenant = tenant,
        };

        // Act
        await store.UpsertAsync(EdictDeadLetterTable.Name, rowKey, row);
        var read = await store.QueryPartitionAsync(EdictDeadLetterTable.Name);

        // Assert
        var stored = Assert.Single(read);
        Assert.Equal(tenant, stored.Tenant);
        Assert.Equal(sourceGrainKey, stored.SourceGrainKey);
    }
}
