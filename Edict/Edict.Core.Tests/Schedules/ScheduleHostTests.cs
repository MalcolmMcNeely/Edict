using Edict.Contracts;
using Edict.Contracts.Configuration;
using Edict.Core.Outbox;
using Edict.Core.Schedules;
using Edict.Core.Tests.TestSupport;

using Microsoft.Extensions.Time.Testing;

namespace Edict.Core.Tests.Schedules;

public sealed class ScheduleHostTests
{
    static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly TimeSpan Period = TimeSpan.FromSeconds(2);

    [Fact]
    public void Schedule_ShouldAddEntry_DueAtNowPlusPeriod()
    {
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(new CallLog());
        var harness = BuildHost(state, new CallLog(), new RecordingScheduleTimer(), (_, _) => Task.CompletedTask);

        harness.Host.Schedule([1, 2, 3], Period);

        var entry = Assert.Single(state.State.Schedule.Active);
        Assert.Equal(Now + Period, entry.DueAt);
        Assert.Equal(Period, entry.Period);
    }

    [Fact]
    public async Task FireDueAsync_ShouldDispatchOnlyEntriesDueNow()
    {
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(new CallLog());
        state.State.Schedule = state.State.Schedule
            .Add(Entry(DueScheduleId, Now))
            .Add(Entry(FutureScheduleId, Now + TimeSpan.FromMinutes(5)));

        var dispatched = new List<Guid>();
        var harness = BuildHost(state, new CallLog(), new RecordingScheduleTimer(), (entry, _) =>
        {
            dispatched.Add(entry.ScheduleId);
            return Task.CompletedTask;
        });

        await harness.Host.FireDueAsync();

        Assert.Equal([DueScheduleId], dispatched);
    }

    [Fact]
    public async Task FireDueAsync_WhenDispatchCompletes_ShouldDisarmTimerAndUnregisterReminder()
    {
        var log = new CallLog();
        var timer = new RecordingScheduleTimer();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(new CallLog());
        state.State.Schedule = state.State.Schedule.Add(Entry(DueScheduleId, Now));

        var harness = BuildHost(state, log, timer, (entry, _) =>
        {
            state.State.Schedule = state.State.Schedule.Complete(entry.ScheduleId);
            return Task.CompletedTask;
        });

        // Arm first so the post-fire reconcile has a reminder to unregister.
        await harness.Host.ReconcileAsync();
        await harness.Host.FireDueAsync();

        Assert.True(timer.DisarmCount > 0, "expected the timer to be disarmed once no schedules remain");
        Assert.True(log.LastIndexOf("UnregisterReminderAsync") >= 0, "expected the reminder to be unregistered");
        Assert.Empty(state.State.Schedule.Active);
    }

    [Fact]
    public async Task FireDueAsync_WhenDispatchContinues_ShouldRearmTimer_AndKeepReminderRegistered()
    {
        var log = new CallLog();
        var timer = new RecordingScheduleTimer();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(new CallLog());
        state.State.Schedule = state.State.Schedule.Add(Entry(DueScheduleId, Now));

        var harness = BuildHost(state, log, timer, (entry, now) =>
        {
            state.State.Schedule = state.State.Schedule.Continue(entry.ScheduleId, now);
            return Task.CompletedTask;
        });

        await harness.Host.FireDueAsync();

        Assert.NotEmpty(timer.Armings);
        Assert.True(log.LastIndexOf("RegisterOrUpdateReminderAsync") >= 0, "expected the reminder to stay registered while a schedule is active");
        Assert.Single(state.State.Schedule.Active);
    }

    [Fact]
    public async Task ReconcileAsync_WithActiveSchedule_ShouldArmTimerForTimeUntilSoonestDue()
    {
        var timer = new RecordingScheduleTimer();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(new CallLog());
        state.State.Schedule = state.State.Schedule.Add(Entry(DueScheduleId, Now + Period));

        var harness = BuildHost(state, new CallLog(), timer, (_, _) => Task.CompletedTask);

        await harness.Host.ReconcileAsync();

        Assert.Equal(Period, Assert.Single(timer.Armings));
    }

    [Fact]
    public async Task ReconcileAsync_WithNoActiveSchedules_ShouldDisarmTimer_AndNotRegisterReminder()
    {
        var log = new CallLog();
        var timer = new RecordingScheduleTimer();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(new CallLog());

        var harness = BuildHost(state, log, timer, (_, _) => Task.CompletedTask);

        await harness.Host.ReconcileAsync();

        Assert.True(timer.DisarmCount > 0);
        Assert.Equal(-1, log.LastIndexOf("RegisterOrUpdateReminderAsync"));
    }

    static readonly Guid DueScheduleId = new("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid FutureScheduleId = new("bbbbbbbb-0000-0000-0000-000000000002");

    static ScheduleEntry Entry(Guid id, DateTimeOffset dueAt) => new()
    {
        ScheduleId = id,
        MessagePayload = [1, 2, 3],
        Period = Period,
        DueAt = dueAt,
    };

    static (ScheduleHost<EdictUnit> Host, FakeTimeProvider Clock) BuildHost(
        CountingPersistentState<GrainEnvelope<EdictUnit>> state,
        CallLog log,
        IScheduleTimer timer,
        Func<ScheduleEntry, DateTimeOffset, Task> dispatch)
    {
        var clock = new FakeTimeProvider(Now);
        var host = new ScheduleHost<EdictUnit>(
            state,
            new RecordingReminderRegistrar(log),
            timer,
            clock,
            new EdictOptions(),
            dispatch);
        return (host, clock);
    }

    sealed class RecordingScheduleTimer : IScheduleTimer
    {
        public List<TimeSpan> Armings { get; } = [];
        public int DisarmCount { get; private set; }

        public void Arm(TimeSpan dueTime, Func<CancellationToken, Task> callback) => Armings.Add(dueTime);

        public void Disarm() => DisarmCount++;
    }
}
