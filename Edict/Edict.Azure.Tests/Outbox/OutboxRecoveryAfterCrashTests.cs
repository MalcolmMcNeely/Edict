using Edict.Contracts.Commands;

namespace Edict.Azure.Tests.Outbox;

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

        Assert.IsType<EdictCommandResult.Accepted>(result);
        Assert.True(AzureControllableOutboxExecutor.FailedAttempts >= 1);

        var probe = fixture.Cluster.GrainFactory.GetGrain<IAzureCounterProbe>(counterId);

        Assert.Equal(1, await probe.GetCountAsync());

        await WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 1);
        Assert.True(await probe.HasDrainReminderAsync());

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
