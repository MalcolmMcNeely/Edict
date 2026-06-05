using Edict.Contracts.Configuration;
using Edict.Core.Outbox;

using Orleans.Runtime;

namespace Edict.Core.Schedules;

/// <summary>
/// Composed Orleans-aware wiring around <see cref="ScheduleSlice"/> — the
/// analogue of <c>OutboxHost</c> for recurring schedules. Arms a grain timer for
/// sub-minute precision while a schedule is active and the <c>edict-schedule</c>
/// Reminder as the one-minute durability backstop, both off the injected
/// <see cref="TimeProvider"/> so the in-memory Test Framework's virtual clock can
/// drive firing. A fire routes the due entry to the grain's dispatch delegate,
/// which re-enters the full handler lifecycle and commits the schedule's next-due
/// advance atomically with state and outbox in the grain's own write — so the
/// host never writes the slice itself; it only mutates it in memory and
/// reconciles the timer and reminder.
/// </summary>
sealed class ScheduleHost<TPayload>
    where TPayload : new()
{
    internal const string ScheduleReminderName = "edict-schedule";

    readonly IPersistentState<GrainEnvelope<TPayload>> _state;
    readonly IReminderRegistrar _reminders;
    readonly IScheduleTimer _timer;
    readonly TimeProvider _timeProvider;
    readonly EdictOptions _options;
    readonly Func<ScheduleEntry, DateTimeOffset, Task> _dispatch;

    bool _reminderRegistered;

    public ScheduleHost(
        IPersistentState<GrainEnvelope<TPayload>> state,
        IReminderRegistrar reminders,
        IScheduleTimer timer,
        TimeProvider timeProvider,
        EdictOptions options,
        Func<ScheduleEntry, DateTimeOffset, Task> dispatch)
    {
        _state = state;
        _reminders = reminders;
        _timer = timer;
        _timeProvider = timeProvider;
        _options = options;
        _dispatch = dispatch;
    }

    public GrainEnvelope<TPayload> State => _state.State;

    /// <summary>
    /// Stages a new schedule, due first at <c>now + period</c>. In-memory only —
    /// the staging is durable because the consumer calls this inside
    /// <c>HandleAsync</c>, whose commit persists the whole envelope; the timer and
    /// Reminder are armed by the post-commit <see cref="ReconcileAsync"/>.
    /// </summary>
    public void Schedule(byte[] messagePayload, TimeSpan period)
    {
        var now = _timeProvider.GetUtcNow();
        State.Schedule = State.Schedule.Add(new ScheduleEntry
        {
            ScheduleId = Guid.NewGuid(),
            MessagePayload = messagePayload,
            Period = period,
            DueAt = now + period,
        });
    }

    public DateTimeOffset? SoonestDueAt => State.Schedule.SoonestDueAt;

    /// <summary>
    /// Fires every schedule due at the current clock through the dispatch
    /// delegate, then reconciles the timer and Reminder against the slice the
    /// fires left behind. Due is snapshotted before dispatch, so a fire that
    /// re-arms or completes its own schedule does not change which entries this
    /// pass fires. Each missed period during a deactivation coalesces into one
    /// fire because the slice holds one entry per schedule.
    /// </summary>
    public async Task FireDueAsync()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var entry in State.Schedule.DueEntries(now))
        {
            await _dispatch(entry, now);
        }

        await ReconcileAsync();
    }

    /// <summary>Reminder tick — the one-minute durability backstop fire path.</summary>
    public Task ReceiveReminderAsync()
    {
        _reminderRegistered = true;
        return FireDueAsync();
    }

    /// <summary>Re-arms the timer and Reminder from durable state when the grain activates.</summary>
    public Task OnActivateAsync() => ReconcileAsync();

    /// <summary>
    /// Arms the timer for the time until the soonest due instant and registers the
    /// Reminder while any schedule is active; disarms and unregisters once none
    /// remain. Called post-commit so the durable state already reflects the change
    /// the timer will act on.
    /// </summary>
    public async Task ReconcileAsync()
    {
        var soonest = State.Schedule.SoonestDueAt;
        if (soonest is null)
        {
            _timer.Disarm();
            await UnregisterReminderAsync();
            return;
        }

        var dueIn = soonest.Value - _timeProvider.GetUtcNow();
        _timer.Arm(dueIn < TimeSpan.Zero ? TimeSpan.Zero : dueIn, _ => FireDueAsync());
        await RegisterReminderAsync();
    }

    async Task RegisterReminderAsync()
    {
        // The Reminder is the durability backstop only — the grain timer carries
        // sub-minute cadence. It reuses the outbox drain reminder period (already
        // validated at or above the Orleans one-minute reminder floor), the same
        // knob the saga cap reminder reuses, so the schedule adds no new tunable.
        var period = _options.OutboxDrainReminderPeriod;
        await _reminders.RegisterOrUpdateReminderAsync(ScheduleReminderName, period, period);
        _reminderRegistered = true;
    }

    async Task UnregisterReminderAsync()
    {
        if (!_reminderRegistered)
        {
            return;
        }

        await _reminders.UnregisterReminderAsync(ScheduleReminderName);
        _reminderRegistered = false;
    }
}
