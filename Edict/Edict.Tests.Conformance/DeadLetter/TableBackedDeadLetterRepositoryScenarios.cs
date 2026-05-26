using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core.DeadLetter;

using Xunit;

namespace Edict.Tests.Conformance.DeadLetter;

/// <summary>
/// The <see cref="TableBackedDeadLetterRepository"/> facade reads
/// <c>EdictDeadLetterEntry</c> rows from the
/// <c>EdictDeadLetterProjectionBuilder.DeadLetterPartition</c> partition via
/// any provider's <see cref="IEdictTableRepository{T}"/>. The four facts here
/// pin the read contract: grain-keyed filter, missing-key empty, list-all,
/// and never-created-table empty.
/// </summary>
public abstract class TableBackedDeadLetterRepositoryScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    static readonly DateTimeOffset FixedDeadLetteredAt =
        new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    static readonly Guid EntryIdA1 = new("00000000-0000-0000-0000-0000000000a1");
    static readonly Guid EntryIdA2 = new("00000000-0000-0000-0000-0000000000a2");
    static readonly Guid EntryIdB1 = new("00000000-0000-0000-0000-0000000000b1");

    readonly TFixture _fixture;

    protected TableBackedDeadLetterRepositoryScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

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
        // The substrate-neutral repository contract tolerates a never-created
        // table — same shape as an existing empty partition.
        var repo = NewRepository(UniqueTable());

        var results = await repo.ListAllAsync();

        Assert.Empty(results);
    }

    IEdictDeadLetterRepository NewRepository(string tableName) =>
        new TableBackedDeadLetterRepository(
            _fixture.GetTableRepository<EdictDeadLetterEntry>(tableName));

    static string UniqueTable() => $"dlt{Guid.NewGuid():N}";

    async Task SeedAsync(string tableName, EdictDeadLetterEntry entry)
    {
        var store = await _fixture.TableStoreFactory.CreateAsync<EdictDeadLetterEntry>(tableName);
        await store.UpsertAsync(
            EdictDeadLetterProjectionBuilder.DeadLetterPartition,
            entry.EntryId.ToString("N"),
            entry);
    }
}
