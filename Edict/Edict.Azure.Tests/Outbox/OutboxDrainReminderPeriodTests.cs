namespace Edict.Azure.Tests.Outbox;

/// <summary>
/// wiring proof on Azurite: <see cref="EdictOptions.OutboxDrainReminderPeriod"/>
/// flows through <c>AddEdict(o =&gt; ...)</c> on the silo, and a failing
/// drain registers the lazy <c>edict-outbox-drain</c> reminder. The pure-
/// logic equivalent in <c>Edict.Core.Tests</c> proved the exact period value
/// passes to <see cref="Edict.Core.Outbox.IReminderRegistrar"/>; this lifted
/// scenario exercises the same option through the full Azurite stack to
/// catch a configuration regression that the unit test cannot see (silo
/// startup, options post-configure, real reminder service).
/// </summary>
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

            // Failing inline drain pins one pending entry and arms the lazy
            // reminder so the drain can recover on a later tick.
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
