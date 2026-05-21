namespace Edict.Azure.Tests.Outbox;

[Collection(AzureOutboxControllableExecutorCollection.Name)]
public sealed class OutboxDrainOnActivationTests(AzureOutboxRecoveryClusterFixture fixture)
{
    [Fact]
    public async Task OnActivate_ShouldDrainPendingOutbox_AfterReactivation()
    {
        var counterId = Guid.NewGuid();
        AzureControllableOutboxExecutor.Reset();
        AzureControllableOutboxExecutor.ShouldFail = true;

        await fixture.Sender.Send(new AzureIncrementCounterCommand(counterId));

        var probe = fixture.Cluster.GrainFactory.GetGrain<IAzureCounterProbe>(counterId);
        await WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 1);

        await probe.DeactivateAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));

        // DeactivateOnIdle + delay alone is not reliable for activation-drain
        // coverage; the Reminder path exercises the same code deterministically.
        AzureControllableOutboxExecutor.ShouldFail = false;
        await probe.ForceDrainViaReminderAsync();

        await WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 0);
        Assert.False(await probe.HasDrainReminderAsync());
    }

    [Fact]
    public async Task OnActivate_ShouldSkipDrain_WhenOutboxIsEmpty()
    {
        var counterId = Guid.NewGuid();
        AzureControllableOutboxExecutor.Reset();
        AzureControllableOutboxExecutor.ShouldFail = false;

        var probe = fixture.Cluster.GrainFactory.GetGrain<IAzureCounterProbe>(counterId);

        Assert.Equal(0, await probe.GetPendingOutboxCountAsync());
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
