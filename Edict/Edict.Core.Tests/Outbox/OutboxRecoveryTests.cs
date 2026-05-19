using Edict.Contracts.Commands;
using Edict.Core.Tests.Grains;

namespace Edict.Core.Tests.Outbox;

// Post-commit publish failure isolation + recovery, end-to-end on an in-memory
// cluster with a virtual clock (ADR 0016/0018). Proves the failure neither
// rolls back nor surfaces, and that the Reminder's drain recovers it.
[Collection(OutboxRecoveryClusterCollection.Name)]
public sealed class OutboxRecoveryTests(OutboxRecoveryClusterFixture fixture)
{
    // Cycle 4/5 — a failing post-commit publish: Send still returns Accepted,
    // State is persisted (no rollback), the entry stays pending (stop-at-head),
    // a Reminder is registered; once downstream heals the Reminder's drain
    // publishes and unregisters itself (zero reminders in steady state).
    [Fact]
    public async Task PostCommitPublishFailure_ShouldNotSurfaceOrRollBack_AndRecoverViaReminder()
    {
        var counterId = Guid.NewGuid();
        ControllableOutboxExecutor.FailedAttempts = 0;
        ControllableOutboxExecutor.ShouldFail = true;

        var result = await fixture.Sender.Send(new IncrementCounterCommand(counterId));

        // Did not surface: caller sees Accepted despite the publish throwing.
        Assert.IsType<EdictCommandResult.Accepted>(result);
        Assert.True(ControllableOutboxExecutor.FailedAttempts >= 1);

        var probe = fixture.Cluster.GrainFactory.GetGrain<ICounterProbe>(counterId);

        // Did not roll back: the State mutation committed before the drain.
        Assert.Equal(1, await probe.GetCountAsync());
        // Stop-at-head: the entry is retained, not acked, not delivered.
        Assert.Equal(1, await probe.GetPendingOutboxCountAsync());
        // Lazy Reminder registered because the Outbox is non-empty.
        Assert.True(await probe.HasDrainReminderAsync());

        // Recovery: downstream healthy, backoff elapsed, the Reminder's drain
        // publishes the still-pending entry and then unregisters itself.
        ControllableOutboxExecutor.ShouldFail = false;
        fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        await probe.ForceDrainViaReminderAsync();

        // The engine only acks (pending → 0) after the executor's publish
        // succeeded, and only unregisters the Reminder once the Outbox is
        // empty — so this is the end-to-end proof the recovery published and
        // wound the lazy Reminder back down to zero (ADR 0018). Inline-path
        // stream delivery itself is covered by the Cycle 1–3 tests.
        Assert.Equal(0, await probe.GetPendingOutboxCountAsync());
        Assert.False(await probe.HasDrainReminderAsync());
    }
}
