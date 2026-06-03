using Edict.Contracts.Commands;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// A post-commit publish failure must not surface from <c>IEdictSender.Send</c>
/// nor roll the state back, and the persisted outbox must let a Reminder-driven
/// drain finish the publish once the substrate recovers — the pending entry has
/// to survive on real persistence. Bound against a fixture whose silo swaps the
/// <c>PublishEventExecutor</c> for <see cref="ControllableOutboxExecutor"/>; the
/// static <c>ShouldFail</c> toggle controls the simulated crash.
/// </summary>
public abstract class OutboxRecoveryAfterCrashScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    readonly TFixture _fixture;

    protected OutboxRecoveryAfterCrashScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PostCommitPublishFailure_ShouldNotSurfaceOrRollBack_AndRecoverViaReminder()
    {
        var counterId = Guid.NewGuid();
        ControllableOutboxExecutor.Reset();
        ControllableOutboxExecutor.ShouldFail = true;

        var result = await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        Assert.IsType<EdictCommandResult.Accepted>(result);
        Assert.True(ControllableOutboxExecutor.FailedAttempts >= 1);

        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);

        Assert.Equal(1, await probe.GetCountAsync());

        await OutboxProbeWaiters.WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 1);
        Assert.True(await probe.HasDrainReminderAsync());

        ControllableOutboxExecutor.ShouldFail = false;
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await probe.ForceDrainViaReminderAsync();

        await OutboxProbeWaiters.WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 0);
        Assert.False(await probe.HasDrainReminderAsync());
    }
}
