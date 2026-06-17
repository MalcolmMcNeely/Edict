using Edict.Contracts.Commands;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core.DeadLetter;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// The inverse of the promotion guarantee: an outbox failure <em>below</em>
/// <c>OutboxMaxAttempts</c> leaves the entry Pending and never promotes — only an
/// attempt that reaches the cap lands a dead-letter row. A count-addressed fault
/// fails just the first attempt, so the sub-cap state can be asserted before the
/// auto-healed retry converges the entry, proving it was a live retryable entry
/// and not a promoted one. Bound against a controllable-executor fixture at
/// <c>OutboxMaxAttempts</c> = 2 with a minimal backoff, so a probe tick's grain
/// round-trip outlasts the backoff and the recovery drain needs no clock gate.
/// </summary>
public abstract class BelowMaxAttemptsNonPromotionScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    readonly TFixture _fixture;

    protected BelowMaxAttemptsNonPromotionScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SubCapFailure_LeavesEntryPending_DoesNotPromote_ThenRecovers()
    {
        // Arrange — fail only the first attempt, so the publish faults once below the cap.
        var counterId = Guid.NewGuid();
        _fixture.OutboxFault.Reset();
        _fixture.OutboxFault.FailUntilAttempt = 1;

        // Act — the inline drain after the send is the failing first attempt.
        var result = await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);
        var deadLetterTable = _fixture.GetTableStore<EdictDeadLetterEntry>(EdictDeadLetterTable.Name);

        // Assert — accepted, the publish faulted, the entry stayed Pending, and no
        // dead-letter row landed: a sub-cap failure does not promote.
        Assert.IsType<EdictCommandResult.Accepted>(result);
        Assert.True(_fixture.OutboxFault.FailedAttempts >= 1);
        Assert.Equal(1, await probe.GetPendingOutboxCountAsync());
        Assert.Empty(await DeadLetterRowsForAsync(deadLetterTable, counterId));

        // Act — drive the recovery drain. The count-addressed fault auto-heals past
        // the first attempt, so a tick re-readies the entry and the publish lands.
        for (var tick = 0; tick < 12 && await probe.GetPendingOutboxCountAsync() > 0; tick++)
        {
            await probe.ForceDrainViaReminderAsync();
        }

        // Assert — converged with no promotion, and the event published exactly once.
        Assert.Equal(0, await probe.GetPendingOutboxCountAsync());
        Assert.Empty(await DeadLetterRowsForAsync(deadLetterTable, counterId));

        var capture = _fixture.GrainFactory.GetGrain<ICounterEventCaptureGrain>(counterId);
        await OutboxProbeWaiters.WaitUntilAsync(async () => (await capture.GetCapturedEventsAsync()).Count == 1);
        var incremented = Assert.IsType<CounterIncrementedEvent>(Assert.Single(await capture.GetCapturedEventsAsync()));
        Assert.Equal(1, incremented.NewCount);
    }

    static async Task<IReadOnlyList<EdictDeadLetterEntry>> DeadLetterRowsForAsync(
        IEdictTableWriteStore<EdictDeadLetterEntry> deadLetterTable, Guid counterId)
    {
        var rows = await deadLetterTable.QueryPartitionAsync(EdictDeadLetterTable.Name);
        return rows.Where(row => row.SourceGrainKey.Contains(counterId.ToString("N"))).ToList();
    }
}
