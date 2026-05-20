using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core.DeadLetter;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.DeadLetter;

// Facade behaviour of the read-only dead-letter repository (ADR 0022). The
// facade is a thin layer over IEdictTableRepository<EdictDeadLetterEntry>:
// ListAllAsync is the partition-wide query; ListAsync filters that result by
// SourceGrainKey. The fake honours the partition key so the facade also has to
// pass the right one ("deadletter") or the partition scan returns nothing.
public sealed class TableBackedDeadLetterRepositoryTests
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
    public async Task ListAllAsync_ShouldReturnEveryEntryInPartition()
    {
        var inner = new InMemoryDeadLetterTable();
        inner.Seed(Entry(EntryIdA1, "grain-A"));
        inner.Seed(Entry(EntryIdB1, "grain-B"));
        var repo = new TableBackedDeadLetterRepository(inner);

        var results = await repo.ListAllAsync();

        await Verify(results.OrderBy(e => e.EntryId))
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task ListAllAsync_ShouldReturnEmpty_WhenPartitionEmpty()
    {
        var repo = new TableBackedDeadLetterRepository(new InMemoryDeadLetterTable());

        var results = await repo.ListAllAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task ListAsync_ShouldReturnEntriesMatchingGrainKey()
    {
        var inner = new InMemoryDeadLetterTable();
        inner.Seed(Entry(EntryIdA1, "grain-A"));
        inner.Seed(Entry(EntryIdB1, "grain-B"));
        inner.Seed(Entry(EntryIdA2, "grain-A"));
        var repo = new TableBackedDeadLetterRepository(inner);

        var results = await repo.ListAsync("grain-A");

        await Verify(results.OrderBy(e => e.EntryId))
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task ListAsync_ShouldReturnEmpty_WhenNoEntriesMatchGrainKey()
    {
        var inner = new InMemoryDeadLetterTable();
        inner.Seed(Entry(EntryIdA1, "grain-A"));
        var repo = new TableBackedDeadLetterRepository(inner);

        var results = await repo.ListAsync("grain-missing");

        Assert.Empty(results);
    }

    // Honours the (partitionKey, rowKey) shape the projection writes — the
    // facade's contract is "query the 'deadletter' partition". A wrong
    // partition key returns empty, which is what proves the facade asks for the
    // right one in the tests above.
    sealed class InMemoryDeadLetterTable : IEdictTableRepository<EdictDeadLetterEntry>
    {
        readonly Dictionary<(string Partition, string Row), EdictDeadLetterEntry> _rows = new();

        public void Seed(EdictDeadLetterEntry entry) =>
            _rows[("deadletter", entry.EntryId.ToString("N"))] = entry;

        public Task<EdictDeadLetterEntry?> GetAsync(
            string partitionKey, string rowKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rows.TryGetValue((partitionKey, rowKey), out var row) ? row : null);

        public Task<IReadOnlyList<EdictDeadLetterEntry>> QueryPartitionAsync(
            string partitionKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EdictDeadLetterEntry>>(
                _rows.Where(kv => kv.Key.Partition == partitionKey).Select(kv => kv.Value).ToList());
    }
}
