using Edict.Azure.TableStorage;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core.DeadLetter;
using Edict.Core.TableStorage;

using Microsoft.Extensions.DependencyInjection;

namespace Edict.Azure.Tests.DeadLetter;

// provider conformance battery for IEdictDeadLetterRepository: the
// same observable behaviour the in-memory facade test proves in Core.Tests,
// re-run against real Azurite via Testcontainers. The repo is
// provider-agnostic — the only thing the Azure provider plugs is the
// IEdictTableRepository<EdictDeadLetterEntry> seam — so the test exercises
// that exact composition: the Core facade wrapping AzureTableRepository over
// the dead-letter table. Per-test unique tables isolate the shared cluster
// fixture (every Edict.Azure.Tests collection shares one Azurite).
[Collection(AzureClusterCollection.Name)]
public sealed class TableBackedDeadLetterRepositoryAzureTests(AzureClusterFixture fixture)
{
    static readonly DateTimeOffset FixedDeadLetteredAt =
        new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    static readonly Guid EntryIdA1 = new("00000000-0000-0000-0000-0000000000a1");
    static readonly Guid EntryIdA2 = new("00000000-0000-0000-0000-0000000000a2");
    static readonly Guid EntryIdB1 = new("00000000-0000-0000-0000-0000000000b1");

    static EdictDeadLetterEntry Entry(Guid id, string sourceGrainKey) => new()
    {
        EntryId = id,
        Kind = "PublishEvent",
        AttemptCount = 8,
        DeadLetteredAt = FixedDeadLetteredAt,
        SourceGrainKey = sourceGrainKey,
        SourceGrainType = "Sample.OrderCommandHandler",
        EffectTarget = "Orders/OrderPlacedEvent",
        TraceParent = null,
        ExceptionType = "System.InvalidOperationException",
        Reason = "downstream unavailable",
        PayloadJson = "{\"OrderId\":\"00000000-0000-0000-0000-000000000099\"}",
    };

    [Fact]
    public async Task ListAsync_ShouldReturnEntriesMatchingGrainKey()
    {
        var tableName = UniqueTable();
        await SeedAsync(tableName, Entry(EntryIdA1, "grain-A"));
        await SeedAsync(tableName, Entry(EntryIdB1, "grain-B"));
        await SeedAsync(tableName, Entry(EntryIdA2, "grain-A"));
        var repo = NewRepository(tableName);

        var results = await repo.ListAsync("grain-A");

        await Verify(results.OrderBy(e => e.EntryId))
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task ListAsync_ShouldReturnEmpty_WhenNoEntriesMatchGrainKey()
    {
        var tableName = UniqueTable();
        await SeedAsync(tableName, Entry(EntryIdA1, "grain-A"));
        var repo = NewRepository(tableName);

        var results = await repo.ListAsync("grain-missing");

        Assert.Empty(results);
    }

    [Fact]
    public async Task ListAllAsync_ShouldReturnEveryEntryInPartition()
    {
        var tableName = UniqueTable();
        await SeedAsync(tableName, Entry(EntryIdA1, "grain-A"));
        await SeedAsync(tableName, Entry(EntryIdB1, "grain-B"));
        var repo = NewRepository(tableName);

        var results = await repo.ListAllAsync();

        await Verify(results.OrderBy(e => e.EntryId))
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task ListAllAsync_ShouldReturnEmpty_WhenPartitionEmpty()
    {
        // AzureTableRepository tolerates the 404 a never-created table returns,
        // which is the same observable shape as an existing empty partition —
        // either is "empty" from the repository's contract perspective.
        var repo = NewRepository(UniqueTable());

        var results = await repo.ListAllAsync();

        Assert.Empty(results);
    }

    [Fact]
    public void AddEdictAzureDeadLetterRepository_ShouldResolveAzureBackedFacade()
    {
        // Proves the wiring acceptance criterion: AddEdict() + the Azure
        // provider's helper together let the client resolve a working,
        // Azure-backed IEdictDeadLetterRepository — exactly the operator's
        // surface.
        var repo = fixture.Cluster.Client.ServiceProvider
            .GetRequiredService<IEdictDeadLetterRepository>();

        Assert.IsType<TableBackedDeadLetterRepository>(repo);
    }

    IEdictDeadLetterRepository NewRepository(string tableName) =>
        new TableBackedDeadLetterRepository(
            new AzureTableRepository<EdictDeadLetterEntry>(fixture.TableServiceClient, tableName));

    static string UniqueTable() => $"dlt{Guid.NewGuid():N}";

    async Task SeedAsync(string tableName, EdictDeadLetterEntry entry)
    {
        IEdictTableStoreFactory factory = new AzureTableWriteStoreFactory(fixture.TableServiceClient);
        var store = await factory.CreateAsync<EdictDeadLetterEntry>(tableName);
        await store.UpsertAsync(
            EdictDeadLetterProjectionBuilder.DeadLetterPartition,
            entry.EntryId.ToString("N"),
            entry);
    }
}
