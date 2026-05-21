using Edict.Contracts.Commands;

namespace Edict.Azure.Tests.Outbox;

/// <summary>
/// Post-commit publish failure on the real Azure Queue stack: a controllable
/// PublishEvent executor throws after the {State, Outbox} commit, so the
/// caller still sees <c>Accepted</c>, the State mutation persists (no
/// rollback), the entry stays pending (stop-at-head), and a lazy Reminder is
/// registered. Once downstream heals and the Reminder fires, the pending
/// entry publishes and the Reminder unwinds to zero.
/// Lifted from <c>OutboxRecoveryTests</c>; now exercises real Azure Blob
/// grain storage and the real reminder service path against Azurite.
/// </summary>
[Collection(AzureOutboxControllableExecutorCollection.Name)]
public sealed class OutboxRecoveryAfterCrashTests(AzureOutboxRecoveryClusterFixture fixture)
{
    [Fact]
    public async Task PostCommitPublishFailure_ShouldNotSurfaceOrRollBack_AndRecoverViaReminder()
    {
        var counterId = Guid.NewGuid();
        AzureControllableOutboxExecutor.Reset();
        AzureControllableOutboxExecutor.ShouldFail = true;

        var result = await fixture.Sender.Send(new AzureIncrementCounterCommand(counterId));

        // Did not surface: caller sees Accepted despite the publish throwing.
        Assert.IsType<EdictCommandResult.Accepted>(result);
        Assert.True(AzureControllableOutboxExecutor.FailedAttempts >= 1);

        var probe = fixture.Cluster.GrainFactory.GetGrain<IAzureCounterProbe>(counterId);

        // Did not roll back: the State mutation committed before the drain.
        Assert.Equal(1, await probe.GetCountAsync());

        // Stop-at-head + lazy Reminder registered because the Outbox is non-empty.
        await WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 1);
        Assert.True(await probe.HasDrainReminderAsync());

        // Recovery: downstream heals, the Reminder's drain publishes the
        // still-pending entry, and once the Outbox is empty it unregisters
        // itself (zero reminders in steady state).
        AzureControllableOutboxExecutor.ShouldFail = false;
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await probe.ForceDrainViaReminderAsync();

        await WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 0);
        Assert.False(await probe.HasDrainReminderAsync());
    }

    static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
