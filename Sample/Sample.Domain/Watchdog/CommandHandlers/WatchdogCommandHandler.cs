using Edict.Contracts.Commands;
using Edict.Contracts.Schedules;
using Edict.Core.Commands;

using Sample.Contracts.Watchdog.Commands;
using Sample.Contracts.Watchdog.Events;
using Sample.Contracts.Watchdog.Messages;
using Sample.Domain.Watchdog.State;

namespace Sample.Domain.Watchdog.CommandHandlers;

/// <summary>
/// Watchdog aggregate, keyed by WatchdogId. On start it schedules a
/// <see cref="WatchdogPoll"/> on a flat cadence with a timeout cap. Each poll
/// advances durable State and asks to fire again; the loop is meant to settle and
/// <c>Complete()</c> before the cap, but a poll that never settles hits the cap and
/// runs <see cref="OnScheduleTimeoutAsync(WatchdogPoll)"/>, which escalates by
/// raising <see cref="WatchdogEscalatedEvent"/> instead of letting the schedule
/// vanish silently.
/// </summary>
public partial class WatchdogCommandHandler : EdictCommandHandler<WatchdogState>
{
    Task<EdictCommandResult> HandleAsync(StartWatchdogCommand command)
    {
        State.WatchdogId = command.WatchdogId;
        Schedule(new WatchdogPoll(), every: TimeSpan.FromHours(1), timeout: TimeSpan.FromMinutes(5));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    Task<EdictScheduleResult> HandleAsync(WatchdogPoll message)
    {
        State.Polls++;
        return Continue();
    }

    Task OnScheduleTimeoutAsync(WatchdogPoll message)
    {
        Raise(new WatchdogEscalatedEvent(State.WatchdogId, State.Polls));
        return Task.CompletedTask;
    }
}
