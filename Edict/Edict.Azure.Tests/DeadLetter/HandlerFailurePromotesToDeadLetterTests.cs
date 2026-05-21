using Edict.Azure.TableStorage;
using Edict.Azure.Tests.Outbox;
using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;

namespace Edict.Azure.Tests.DeadLetter;

/// <summary>
/// Full-loop coverage of ADR 0022 against the real Azure stack: a permanent
/// publish failure on an aggregate's raised event drives the engine through
/// <see cref="Contracts.Configuration.EdictOptions.OutboxMaxAttempts"/> retries
/// and into the promotion path. The engine swaps the failing entry for an
/// <see cref="EdictDeadLetterRaised"/> PublishEvent entry in the same commit;
/// the controllable is then healed so the dead-letter publish itself succeeds;
/// the framework-shipped <see cref="EdictDeadLetterProjectionBuilder"/> consumes
/// the stream and upserts a row to the literal <c>"deadletter"</c> Azure Table.
/// <para>
/// Lifted from <c>Edict.Core.Tests/DeadLetter/DeadLetterEndToEndTests</c>
/// (now removed) — the in-memory cluster did not exercise the real Azure Queue
/// + Azure Blob transport ADR 0022 is meant to survive (ADR 0029). The smoke
/// version in <c>Outbox/OutboxDeadLetterPromotionTests</c> only asserted
/// <c>SourceGrainKey</c>; this version pins every RCA field plus a Verify
/// snapshot so any drift in the promoted row's shape trips CI.
/// </para>
/// </summary>
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

        // Drive reminder ticks until the original entry has failed twice (the
        // MaxAttempts=2 threshold). Each drain bumps the per-entry backoff by
        // 200ms (OutboxBaseDelay), so we wait through that gate between forced
        // drains.
        await WaitUntilAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            await probe.ForceDrainViaReminderAsync();
            return AzureControllableOutboxExecutor.FailedAttempts >= 2;
        });

        // The host has promoted the original entry to a dead-letter PublishEvent
        // (EdictDeadLetterRaised) at the tail. Heal the controllable so the
        // dead-letter event itself publishes — without this flip the controllable
        // would fail every promoted entry too, looping promotions and never
        // landing the row (ADR 0026: the promoted entry rides the same backoff /
        // max-attempts rules as any other PublishEvent).
        AzureControllableOutboxExecutor.ShouldFail = false;

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

        // Pin the stable RCA fields with a single snapshot. EntryId,
        // DeadLetteredAt, TraceParent, PayloadJson, SourceGrainKey, and
        // SourceEventId are volatile (Guid/clock/trace-driven) so they are
        // scrubbed; their structural shape is checked above through the
        // dedicated Asserts.
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
