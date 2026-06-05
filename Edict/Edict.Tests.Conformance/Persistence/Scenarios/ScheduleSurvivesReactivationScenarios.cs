using Edict.Contracts.Schedules;
using Edict.Tests.Conformance.Schedules;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// A schedule's durable <c>ScheduleEntry</c> is part of the framework-owned grain
/// state, so it must survive a deactivation: after the grain idles out and comes
/// back from the provider's real grain storage, the catch-up-on-activation path
/// (<c>OnActivateAsync</c> reconciles the schedule and arms the grain timer at the
/// now-overdue instant) fires the work that fell due while the grain was gone. The
/// scenario drives the due and deadline instants past on the fixture's virtual
/// clock and reactivates the grain, so it exercises that on-activation seam, never
/// the one-minute Reminder. The reference stream is only the delivery vehicle; the
/// asserted property is the schedule's durable survival across reactivation.
/// </summary>
public abstract class ScheduleSurvivesReactivationScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture, IScheduleClockFixture
{
    static readonly TimeSpan Cadence = TimeSpan.FromHours(1);

    readonly TFixture _fixture;

    protected ScheduleSurvivesReactivationScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OverdueTick_ShouldFireOnActivation_AfterReactivation()
    {
        var key = Guid.NewGuid();
        // Uncapped so only the recurring tick is overdue when the grain comes back.
        await _fixture.Sender.SendAsync(new ArmScheduleCommand(key, Cadence, EdictSchedule.Unbounded));

        var probe = _fixture.GrainFactory.GetGrain<IScheduleConformanceProbe>(key);
        Assert.NotNull(await probe.PeekSoonestDueAsync());

        // Push the clock a full cadence past the first due instant, then let the
        // grain idle out so its in-memory timer is gone — the only thing that can
        // fire the overdue tick now is the catch-up on the next activation.
        _fixture.AdvanceClock(TimeSpan.FromTicks(Cadence.Ticks * 2));

        var activationBeforeDeactivation = await probe.GetActivationIdAsync();
        await probe.DeactivateAsync();
        await WaitUntilAsync(async () => await probe.GetActivationIdAsync() != activationBeforeDeactivation);

        await WaitUntilAsync(async () => await probe.GetTickCountAsync() == 1);

        Assert.Equal(1, await probe.GetTickCountAsync());
        // The tick re-armed for the next cadence; a Continue keeps the schedule live.
        Assert.NotNull(await probe.PeekSoonestDueAsync());
    }

    [Fact]
    public async Task OverdueTimeout_ShouldFireOnActivation_AfterReactivation()
    {
        var key = Guid.NewGuid();
        // Cap sooner than the first tick so the overdue instant that survives the
        // deactivation is the deadline, not a due tick.
        await _fixture.Sender.SendAsync(new ArmScheduleCommand(key, Cadence, TimeSpan.FromMinutes(30)));

        var probe = _fixture.GrainFactory.GetGrain<IScheduleConformanceProbe>(key);
        Assert.NotNull(await probe.PeekSoonestDueAsync());

        _fixture.AdvanceClock(TimeSpan.FromTicks(Cadence.Ticks * 2));

        var activationBeforeDeactivation = await probe.GetActivationIdAsync();
        await probe.DeactivateAsync();
        await WaitUntilAsync(async () => await probe.GetActivationIdAsync() != activationBeforeDeactivation);

        await WaitUntilAsync(async () => await probe.GetTimeoutCountAsync() == 1);

        Assert.Equal(1, await probe.GetTimeoutCountAsync());
        // The timeout is terminal: the cap ran the compensation hook and removed the
        // schedule, so no tick fired and nothing remains due.
        Assert.Equal(0, await probe.GetTickCountAsync());
        Assert.Null(await probe.PeekSoonestDueAsync());
    }

    static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutSeconds = 30)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
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
