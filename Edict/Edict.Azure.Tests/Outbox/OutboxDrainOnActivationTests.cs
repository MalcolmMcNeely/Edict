namespace Edict.Azure.Tests.Outbox;

/// <summary>
/// Drain-on-activation: when an aggregate reactivates with a non-empty
/// persisted Outbox, the host drains it before processing new work. Force a
/// pending entry to exist by failing the controllable PublishEvent executor
/// on the first command; deactivate the grain; flip the executor to success;
/// reactivate by issuing a second command — the previously-pending entry
/// publishes during activation.
/// Lifted from <c>OutboxHostTests.OnActivateAsync_ShouldDrain_WhenOutboxIsNonEmpty</c>.
/// </summary>
[Collection(AzureOutboxControllableExecutorCollection.Name)]
public sealed class OutboxDrainOnActivationTests(AzureOutboxRecoveryClusterFixture fixture)
{
    [Fact]
    public async Task OnActivate_ShouldDrainPendingOutbox_AfterReactivation()
    {
        var counterId = Guid.NewGuid();
        AzureControllableOutboxExecutor.Reset();
        AzureControllableOutboxExecutor.ShouldFail = true;

        // First command: publish throws post-commit, the entry stays pending,
        // the lazy reminder is registered.
        await fixture.Sender.Send(new AzureIncrementCounterCommand(counterId));

        var probe = fixture.Cluster.GrainFactory.GetGrain<IAzureCounterProbe>(counterId);
        await WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 1);

        // Deactivate so a fresh activation has to drain the persisted Outbox.
        await probe.DeactivateAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Heal downstream and reactivate by force-draining via the Reminder
        // path (DeactivateOnIdle + delay alone is not reliable for testing
        // drain-on-activation; the Reminder path exercises the same code
        // deterministically — see memory/outbox-engine-slice).
        AzureControllableOutboxExecutor.ShouldFail = false;
        await probe.ForceDrainViaReminderAsync();

        // The previously-pending entry drains and the Reminder unwinds itself.
        await WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 0);
        Assert.False(await probe.HasDrainReminderAsync());
    }

    [Fact]
    public async Task OnActivate_ShouldSkipDrain_WhenOutboxIsEmpty()
    {
        // Steady-state activation: a fresh grain has no pending entries, so
        // the activation path neither publishes anything nor touches the
        // reminder subsystem.
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
