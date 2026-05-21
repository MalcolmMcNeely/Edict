namespace Edict.Azure.Tests.Outbox;

[Collection(AzureOutboxControllableExecutorCollection.Name)]
public sealed class OutboxDrainReminderPeriodTests(AzureOutboxReminderPeriodClusterFixture fixture)
{
    [Fact]
    public async Task FailingDrain_ShouldRegisterLazyReminder()
    {
        var counterId = Guid.NewGuid();
        AzureControllableOutboxExecutor.Reset();
        AzureControllableOutboxExecutor.ShouldFail = true;

        try
        {
            await fixture.Sender.Send(new AzureIncrementCounterCommand(counterId));

            var probe = fixture.Cluster.GrainFactory.GetGrain<IAzureCounterProbe>(counterId);

            await WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 1);
            Assert.True(await probe.HasDrainReminderAsync());
        }
        finally
        {
            AzureControllableOutboxExecutor.Reset();
        }
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
