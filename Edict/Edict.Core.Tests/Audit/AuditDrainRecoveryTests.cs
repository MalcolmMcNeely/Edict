using System.Diagnostics;

using Edict.Contracts.Audit;
using Edict.Core.Tests.Grains;

using Xunit;

namespace Edict.Core.Tests.Audit;

// The crash-recovery property a fake-state unit test cannot vouch for: a record
// staged in grain state, with its post-commit drain faulted, must still reach the
// WORM store when the grain reactivates, driven by the real on-activation recovery
// against a real grain, reminder service, and persistence.
[Collection(AuditDrainRecoveryCollection.Name)]
public sealed class AuditDrainRecoveryTests(AuditDrainRecoveryClusterFixture fixture)
{
    static string EntityType => typeof(CounterAggregate).FullName!;

    [Fact]
    public async Task StagedRecord_WhenDrainFailsThenGrainDeactivates_ReachesWormStoreOnReactivation()
    {
        // Arrange — fault the next payload write so the post-commit drain leaves the
        // record staged with nothing in either store.
        var counterId = Guid.NewGuid();
        var entityKey = counterId.ToString();
        fixture.PayloadStore.ThrowOnNextPut = true;

        // Act — staging commits the record; the post-commit drain then faults.
        await fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        // Wait for the faulted drain to consume the toggle, so the records are proven
        // staged-but-undrained before deactivation. The accepted increment captures
        // one C1 command record and one E1 event record, so two records stage.
        await WaitUntilAsync(() => !fixture.PayloadStore.ThrowOnNextPut);
        var probe = fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);
        Assert.Equal(2, await probe.GetPendingAuditCountAsync());
        Assert.Equal(0, fixture.AuditStore.Count);

        // Deactivate before any drain reaches the store, then reactivate by touching
        // the grain — on-activation recovery is the only path that can drain it now.
        await probe.DeactivateAsync();
        await probe.GetCountAsync();

        // Assert — both records reached the WORM store through recovery.
        var records = await WaitForRecordsAsync(entityKey, expectedCount: 2);
        Assert.All(records, record => Assert.Equal(counterId, Guid.Parse(record.EntityKey)));
    }

    async Task<IReadOnlyList<EdictAuditRecord>> WaitForRecordsAsync(string entityKey, int expectedCount)
    {
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 10;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            var records = await fixture.AuditStore.ByEntityAsync(EntityType, entityKey, CancellationToken.None);
            if (records.Count >= expectedCount)
            {
                return records;
            }

            await Task.Delay(25);
        }

        Assert.Fail($"Expected {expectedCount} audit records for {entityKey} but recovery did not produce them in time.");
        return [];
    }

    static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 10;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail("Condition was not met in time.");
    }
}
