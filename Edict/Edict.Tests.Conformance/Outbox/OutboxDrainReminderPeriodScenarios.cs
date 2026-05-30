using Xunit;

namespace Edict.Tests.Conformance.Outbox;

/// <summary>
/// When the inline drain fails, the outbox host must register a lazy drain
/// reminder so that a subsequent activation can recover. Bound against a
/// fixture wired with <see cref="ControllableOutboxExecutor"/> and a reminder
/// period long enough for the test to observe the reminder before it fires.
/// </summary>
public abstract class OutboxDrainReminderPeriodScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected OutboxDrainReminderPeriodScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FailingDrain_ShouldRegisterLazyReminder()
    {
        var counterId = Guid.NewGuid();
        ControllableOutboxExecutor.Reset();
        ControllableOutboxExecutor.ShouldFail = true;

        try
        {
            await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

            var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);

            await OutboxProbeWaiters.WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 1);
            Assert.True(await probe.HasDrainReminderAsync());
        }
        finally
        {
            ControllableOutboxExecutor.Reset();
        }
    }
}
