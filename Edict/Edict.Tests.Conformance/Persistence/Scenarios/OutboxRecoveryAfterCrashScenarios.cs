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
/// fixture's <see cref="OutboxFaultState.ShouldFail"/> switch controls the
/// simulated crash.
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
        _fixture.OutboxFault.Reset();
        _fixture.OutboxFault.ShouldFail = true;

        var result = await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        Assert.IsType<EdictCommandResult.Accepted>(result);
        Assert.True(_fixture.OutboxFault.FailedAttempts >= 1);

        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);

        Assert.Equal(1, await probe.GetCountAsync());

        await OutboxProbeWaiters.WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 1);
        Assert.True(await probe.HasDrainReminderAsync());

        // Recovery is Reminder-driven: once the fault heals, a drain retries the
        // pending publish the moment the entry's backoff comes due. The poll drives
        // that drain each iteration until it lands.
        _fixture.OutboxFault.ShouldFail = false;
        await ConformanceWaiters.WaitUntilAsync(async () =>
        {
            await probe.ForceDrainViaReminderAsync();
            return await probe.GetPendingOutboxCountAsync() == 0;
        });
        Assert.False(await probe.HasDrainReminderAsync());
    }
}
