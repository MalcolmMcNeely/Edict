using Edict.Contracts.Commands;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Projections;
using Edict.Core.DeadLetter;

namespace Edict.Core.Tests.DeadLetter;

public class GrainBackedDeadLetterRepositoryTests
{
    sealed class StubReader(IReadOnlyList<EdictDeadLetterEntry> rows) : IEdictListProjectionReader<EdictDeadLetterEntry>
    {
        public string? LastPartitionKey { get; private set; }

        public Task<EdictProjectionRead<EdictDeadLetterEntry>> GetAsync(
            string partitionKey, string rowKey, EdictCursor? after = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EdictProjectionPartitionRead<EdictDeadLetterEntry>> QueryPartitionAsync(
            string partitionKey, EdictCursor? after = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            LastPartitionKey = partitionKey;
            return Task.FromResult(new EdictProjectionPartitionRead<EdictDeadLetterEntry>(rows, EdictReadStatus.Immediate));
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
