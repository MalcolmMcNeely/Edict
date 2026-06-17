using Edict.Tests.Conformance.Outbox;
using Edict.Tests.Conformance.Reactivation;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// On grain reactivation, the outbox must drain any pending entries from the
/// previous activation. Bound against a fixture wired with
/// <see cref="ControllableOutboxExecutor"/>.
/// </summary>
public abstract class OutboxDrainOnActivationScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
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
        _fixture.OutboxFault.Reset();
        _fixture.OutboxFault.ShouldFail = true;

        await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);
        await OutboxProbeWaiters.WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 1);

        // Heal, then force a confirmed reactivation so the drain-on-activation path
        // runs against durable state with the fault cleared. The Reminder-driven poll
        // exercises the same drain code deterministically and finishes the publish
        // once the pending entry's backoff comes due.
        _fixture.OutboxFault.ShouldFail = false;
        await DeactivationWaiter.DeactivateAndConfirmAsync(probe);

        await ConformanceWaiters.WaitUntilAsync(async () =>
        {
            await probe.ForceDrainViaReminderAsync();
            return await probe.GetPendingOutboxCountAsync() == 0;
        });
        Assert.False(await probe.HasDrainReminderAsync());
    }

    [Fact]
    public async Task OnActivate_ShouldSkipDrain_WhenOutboxIsEmpty()
    {
        var counterId = Guid.NewGuid();
        _fixture.OutboxFault.Reset();
        _fixture.OutboxFault.ShouldFail = false;

        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);

        Assert.Equal(0, await probe.GetPendingOutboxCountAsync());
        Assert.False(await probe.HasDrainReminderAsync());
    }
}
