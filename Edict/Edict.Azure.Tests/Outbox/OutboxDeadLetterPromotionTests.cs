using Edict.Azure.TableStorage;
using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;

namespace Edict.Azure.Tests.Outbox;

/// <summary>
/// Dead-letter promotion + drain-continues on the real transport: with
/// <see cref="Contracts.Configuration.EdictOptions.OutboxMaxAttempts"/> at 2
/// and a permanently-failing PublishEvent executor, two failing attempts
/// trip the promotion threshold. The host removes the failing entry, appends
/// a synthetic <c>EdictDeadLetterRaised</c> PublishEvent at the tail, and
/// drains that — landing a row in the Azure Table dead-letter repository
/// (ADR 0022). The dead-letter row's presence is the end-to-end proof the
/// host walked through the failing entry, promoted it, and continued the
/// drain. Lifted from <c>OutboxHostTests</c>' promotion suite, condensed:
/// the slice-level "single state write" / "append at tail" / "backoff
/// promoted entry" invariants are <i>type-level</i> contracts already proven
/// by surviving pure-logic <c>OutboxSliceTests</c>.
/// </summary>
[Collection(AzureOutboxControllableExecutorCollection.Name)]
public sealed class OutboxDeadLetterPromotionTests(AzureOutboxDeadLetterClusterFixture fixture)
{
    [Fact]
    public async Task DrainAsync_ShouldPromoteFailingEntry_AfterMaxAttemptsExceeded()
    {
        var counterId = Guid.NewGuid();
        AzureControllableOutboxExecutor.Reset();
        AzureControllableOutboxExecutor.ShouldFail = true;

        await fixture.Sender.Send(new AzureIncrementCounterCommand(counterId));

        var probe = fixture.Cluster.GrainFactory.GetGrain<IAzureCounterProbe>(counterId);

        // Drive reminder ticks until the original entry has failed twice (the
        // MaxAttempts=2 threshold). Each drain bumps the per-entry backoff by
        // 200ms (OutboxBaseDelay in the fixture), so we wait through that gate
        // between forced drains.
        await WaitUntilAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            await probe.ForceDrainViaReminderAsync();
            return AzureControllableOutboxExecutor.FailedAttempts >= 2;
        });

        // The host has promoted the original entry to a dead-letter
        // PublishEvent (EdictDeadLetterRaised) at the tail. Heal the
        // controllable so the dead-letter event actually publishes — without
        // this flip the controllable would fail every promoted entry too,
        // looping promotions and never landing the row (ADR 0026: the
        // promoted entry is just another PublishEvent entry under the same
        // backoff / max-attempts rules).
        AzureControllableOutboxExecutor.ShouldFail = false;

        // Drain the now-publishable dead-letter entry; the projection builder
        // upserts the dead-letter row on the Azure Table.
        await WaitUntilAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            await probe.ForceDrainViaReminderAsync();
            return await probe.GetPendingOutboxCountAsync() == 0;
        });

        // The EdictDeadLetterProjectionBuilder writes to its literal TableName
        // ("deadletter") — independent of the fixture's per-collection
        // DeadLetterTableName (which backs the operator-facing repository
        // facade). Read directly from the literal table to observe the row.
        var deadLetterRepo = new AzureTableRepository<EdictDeadLetterEntry>(
            fixture.TableServiceClient,
            EdictDeadLetterProjectionBuilder.DeadLetterPartition);

        await WaitUntilAsync(async () =>
        {
            var entries = await deadLetterRepo.QueryPartitionAsync(
                EdictDeadLetterProjectionBuilder.DeadLetterPartition);
            return entries.Any(e => e.SourceGrainKey.Contains(counterId.ToString()));
        });

        var allEntries = await deadLetterRepo.QueryPartitionAsync(
            EdictDeadLetterProjectionBuilder.DeadLetterPartition);
        Assert.Contains(allEntries, e => e.SourceGrainKey.Contains(counterId.ToString()));
    }

    static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(300));
        }
    }
}
