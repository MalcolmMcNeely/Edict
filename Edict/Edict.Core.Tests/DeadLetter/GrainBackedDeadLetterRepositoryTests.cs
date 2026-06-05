using Edict.Contracts.DeadLetter;
using Edict.Contracts.Projections;
using Edict.Core.DeadLetter;

namespace Edict.Core.Tests.DeadLetter;

public class GrainBackedDeadLetterRepositoryTests
{
    sealed class StubReader(IReadOnlyList<EdictDeadLetterEntry> rows) : IEdictProjectionReader<EdictDeadLetterEntry>
    {
        public string? LastPartitionKey { get; private set; }

        public Task<EdictDeadLetterEntry?> GetAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EdictDeadLetterEntry>> QueryPartitionAsync(string partitionKey, CancellationToken cancellationToken = default)
        {
            LastPartitionKey = partitionKey;
            return Task.FromResult(rows);
        }
    }

    static EdictDeadLetterEntry EntryFrom(string sourceGrainKey) =>
        new() { EntryId = Guid.NewGuid(), SourceGrainKey = sourceGrainKey };

    [Fact]
    public async Task ListAllAsync_ShouldQueryTheSingletonPartition()
    {
        var reader = new StubReader([EntryFrom("a"), EntryFrom("b")]);
        var repository = new GrainBackedDeadLetterRepository(reader);

        var all = await repository.ListAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal(EdictDeadLetterRaised.SingletonGrainKey.ToString(), reader.LastPartitionKey);
    }

    [Fact]
    public async Task ListAsync_ShouldReturnOnlyEntriesMatchingTheGrainKey()
    {
        var reader = new StubReader([EntryFrom("order-1"), EntryFrom("order-2"), EntryFrom("order-1")]);
        var repository = new GrainBackedDeadLetterRepository(reader);

        var matches = await repository.ListAsync("order-1");

        Assert.Equal(2, matches.Count);
        Assert.All(matches, entry => Assert.Equal("order-1", entry.SourceGrainKey));
    }
}
