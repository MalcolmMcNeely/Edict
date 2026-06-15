using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Persistence-axis conformance that the per-aggregate chain head is durable: after
/// the command-handler grain is forced to deactivate, the next command reloads the
/// chain head from durable grain state rather than memory, so the records it captures
/// continue the same chain and the whole chain still verifies. This is the audit
/// analogue of read-your-writes surviving a reactivation against a real grain-state
/// store.
/// </summary>
public abstract class AuditChainReactivationScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture, IAuditConformanceFixture
{
    readonly TFixture _fixture;

    protected AuditChainReactivationScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    static string EntityType => typeof(CounterAggregate).FullName!;

    [Fact]
    public async Task Chain_ShouldContinueAcrossGrainReactivation()
    {
        // Arrange
        var counterId = Guid.NewGuid();
        var entityKey = counterId.ToString();
        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);

        // Increment (C1 + E1) then reject (C1) — three records before deactivation.
        await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));
        await _fixture.Sender.SendAsync(new RejectCounterCommand(counterId));
        await AuditConformanceWaiters.WaitForEntityRecordsAsync(_fixture, EntityType, entityKey, expectedCount: 3);

        // Act — force the grain to deactivate so the next command reloads the
        // chain head from durable state rather than memory.
        var activationBeforeDeactivation = await probe.GetActivationIdAsync();
        await probe.DeactivateAsync();
        await AuditConformanceWaiters.WaitUntilAsync(async () => await probe.GetActivationIdAsync() != activationBeforeDeactivation);

        // A second increment after reactivation adds another C1 + E1.
        await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        // Assert — the post-reactivation records continue the chain (seq 3, 4) and verify.
        var records = await AuditConformanceWaiters.WaitForEntityRecordsAsync(_fixture, EntityType, entityKey, expectedCount: 5);
        Assert.Equal([0L, 1L, 2L, 3L, 4L], records.Select(record => record.Sequence));

        var verification = await _fixture.VerifyChainAsync(EntityType, entityKey);
        Assert.True(verification.IsIntact);
    }
}
