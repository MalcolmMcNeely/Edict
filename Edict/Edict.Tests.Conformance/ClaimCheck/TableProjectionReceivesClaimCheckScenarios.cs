using Edict.Contracts.TableStorage;

using Xunit;

namespace Edict.Tests.Conformance.ClaimCheck;

/// <summary>
/// Receiver-side claim-check conformance for a table-projection builder: a
/// pointer-bearing <c>EdictEventEnvelope</c> reaches
/// <c>ClaimCheckProjectionBuilder</c> via the real substrate stream and
/// dispatches through the deferred <c>InvokeHandler</c> path. The projection's
/// staged <c>UpsertRow</c> effect must survive that path — the row lands in the
/// substrate's store, readable back through the provider's
/// <c>IEdictTableRepository{T}</c>.
/// </summary>
public abstract class TableProjectionReceivesClaimCheckScenarios<TFixture>
    where TFixture : ClaimCheckFixture
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
        var repository = _fixture.GetTableRepository<ClaimCheckProjectionRow>(ClaimCheckProjectionBuilder.Table);

        await _fixture.Sender.SendAsync(new IncrementClaimCheckCounterCommand(counterId, payload));

        var row = await WaitForRowAsync(repository, counterId.ToString());

        Assert.NotNull(row);
        Assert.Equal(1, row.Count);
        Assert.Equal(payload, row.Payload);
    }

    static async Task<ClaimCheckProjectionRow?> WaitForRowAsync(
        IEdictTableRepository<ClaimCheckProjectionRow> repository, string key, int timeoutSeconds = 30)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await repository.GetAsync(key, key);
            if (row is not null)
            {
                return row;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return await repository.GetAsync(key, key);
    }
}
