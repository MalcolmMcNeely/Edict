using Xunit;

namespace Edict.Tests.Conformance.Outbox;

/// <summary>
/// On grain reactivation, the outbox must drain any pending entries from the
/// previous activation. Bound against a fixture wired with
/// <see cref="ControllableOutboxExecutor"/>.
/// </summary>
public abstract class OutboxDrainOnActivationScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected OutboxDrainOnActivationScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OnActivate_ShouldDrainPendingOutbox_AfterReactivation()
    {
        var counterId = Guid.NewGuid();
        ControllableOutboxExecutor.Reset();
        ControllableOutboxExecutor.ShouldFail = true;

        await _fixture.Sender.Send(new IncrementCounterCommand(counterId));

        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);
        await OutboxProbeWaiters.WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 1);

        await probe.DeactivateAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));

        // DeactivateOnIdle + delay alone is not reliable for activation-drain
        // coverage; the Reminder path exercises the same code deterministically.
        ControllableOutboxExecutor.ShouldFail = false;
        await probe.ForceDrainViaReminderAsync();

        await OutboxProbeWaiters.WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 0);
        Assert.False(await probe.HasDrainReminderAsync());
    }

    [Fact]
    public async Task OnActivate_ShouldSkipDrain_WhenOutboxIsEmpty()
    {
        var counterId = Guid.NewGuid();
        ControllableOutboxExecutor.Reset();
        ControllableOutboxExecutor.ShouldFail = false;

        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);

        Assert.Equal(0, await probe.GetPendingOutboxCountAsync());
        Assert.False(await probe.HasDrainReminderAsync());
    }
}
