using Edict.Azure.TableStorage;
using Edict.Azure.Tests.Outbox;
using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;

namespace Edict.Azure.Tests.DeadLetter;

[Collection(AzureOutboxControllableExecutorCollection.Name)]
public sealed class HandlerFailurePromotesToDeadLetterTests(AzureOutboxDeadLetterClusterFixture fixture)
{
    [Fact]
    public async Task Promotes_ShouldLandRowWithRcaFields()
    {
        var counterId = Guid.NewGuid();
        AzureControllableOutboxExecutor.Reset();
        AzureControllableOutboxExecutor.ShouldFail = true;

        await fixture.Sender.Send(new AzureIncrementCounterCommand(counterId));

        var probe = fixture.Cluster.GrainFactory.GetGrain<IAzureCounterProbe>(counterId);

        // Drive reminder ticks until the entry hits MaxAttempts=2. Each drain
        // bumps backoff by OutboxBaseDelay (200ms), so the loop waits past
        // that gate between forced drains.
        await WaitUntilAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            await probe.ForceDrainViaReminderAsync();
            return AzureControllableOutboxExecutor.FailedAttempts >= 2;
        });

        // Heal the controllable so the promoted EdictDeadLetterRaised entry
        // can publish — otherwise it would loop on the same fail/promote
        // cycle and never land the row.
        AzureControllableOutboxExecutor.ShouldFail = false;

        await WaitUntilAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            await probe.ForceDrainViaReminderAsync();
            return await probe.GetPendingOutboxCountAsync() == 0;
        });

        // The projection writes to its literal "deadletter" table —
        // independent of the fixture's per-collection DeadLetterTableName
        // (which backs the operator-facing repository facade).
        var deadLetterTable = new AzureTableRepository<EdictDeadLetterEntry>(
            fixture.TableServiceClient,
            EdictDeadLetterProjectionBuilder.DeadLetterPartition);

        await WaitUntilAsync(async () =>
        {
            var entries = await deadLetterTable.QueryPartitionAsync(
                EdictDeadLetterProjectionBuilder.DeadLetterPartition);
            return entries.Any(e => e.SourceGrainKey.Contains(counterId.ToString()));
        });

        var allEntries = await deadLetterTable.QueryPartitionAsync(
            EdictDeadLetterProjectionBuilder.DeadLetterPartition);
        var entry = allEntries.Single(e => e.SourceGrainKey.Contains(counterId.ToString()));

        Assert.Equal("PublishEvent", entry.Kind);
        Assert.Equal(counterId.ToString(), entry.SourceGrainKey);
        Assert.Contains("AzureCounterAggregate", entry.SourceGrainType);
        Assert.Equal("AzureCounters/AzureCounterIncrementedEvent", entry.EffectTarget);
        Assert.Equal("System.InvalidOperationException", entry.ExceptionType);
        Assert.Equal("controllable publish failure (azure outbox test)", entry.Reason);
        Assert.NotNull(entry.PayloadJson);

        await Verify(entry).DontScrubGuids().DontScrubDateTimes()
            .ScrubMember<EdictDeadLetterEntry>(e => e.EntryId)
            .ScrubMember<EdictDeadLetterEntry>(e => e.DeadLetteredAt)
            .ScrubMember<EdictDeadLetterEntry>(e => e.TraceParent)
            .ScrubMember<EdictDeadLetterEntry>(e => e.PayloadJson)
            .ScrubMember<EdictDeadLetterEntry>(e => e.SourceGrainKey)
            .ScrubMember<EdictDeadLetterEntry>(e => e.SourceEventId);
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
