using Edict.Contracts.TableStorage;
using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// The persistence half of receiver-side claim-check for a table-projection
/// builder: a pointer-bearing <c>EdictEventEnvelope</c> reaches
/// <c>ClaimCheckProjectionBuilder</c> via the reference stream, dispatches
/// through the deferred <c>InvokeHandler</c> path, and the projection's staged
/// <c>UpsertRow</c> effect lands a row in the real store, readable back through
/// the provider's <see cref="IEdictTableWriteStore{T}"/>. The receiver-unwrap
/// half is the streaming axis's job; here the asserted artefact is the durable
/// row.
/// </summary>
public abstract class TableProjectionReceivesClaimCheckScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    readonly TFixture _fixture;

    protected TableProjectionReceivesClaimCheckScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PointerEnvelope_ShouldWriteProjectionRow()
    {
        var counterId = Guid.NewGuid();
        var payload = $"projection-{Guid.NewGuid():N}";
        var repository = _fixture.GetTableStore<ClaimCheckProjectionRow>(ClaimCheckProjectionBuilder.Table);

        await _fixture.Sender.SendAsync(new IncrementClaimCheckCounterCommand(counterId, payload));

        // The projection grain's PartitionKey is its own composed key (the route
        // Guid in "N" form); its consumer-chosen RowKey is the counter Guid in "D"
        // form. They are distinct coordinates, so the read addresses each in the
        // form the builder wrote it.
        var row = await WaitForRowAsync(repository, counterId.ToString("N"), counterId.ToString());

        Assert.NotNull(row);
        Assert.Equal(1, row.Count);
        Assert.Equal(payload, row.Payload);
    }

    static async Task<ClaimCheckProjectionRow?> WaitForRowAsync(
        IEdictTableWriteStore<ClaimCheckProjectionRow> repository, string partitionKey, string rowKey, int timeoutSeconds = 30)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await repository.GetAsync(partitionKey, rowKey);
            if (row is not null)
            {
                return row;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return await repository.GetAsync(partitionKey, rowKey);
    }
}
