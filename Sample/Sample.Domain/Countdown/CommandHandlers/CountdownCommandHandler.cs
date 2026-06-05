using Edict.Contracts.Commands;
using Edict.Contracts.Schedules;
using Edict.Core.Commands;

using Sample.Contracts.Countdown.Commands;
using Sample.Contracts.Countdown.Events;
using Sample.Contracts.Countdown.Messages;
using Sample.Domain.Countdown.State;

namespace Sample.Domain.Countdown.CommandHandlers;

/// <summary>
/// Countdown aggregate, keyed by CountdownId. On start it schedules a
/// <see cref="CountdownTick"/> on a flat cadence; each fire decrements the
/// remaining count, raises <see cref="CountdownTickedEvent"/>, and on reaching
/// zero raises <see cref="CountdownFinishedEvent"/> and completes the schedule.
/// The whole loop is durable and survives deactivation: the cadence is declared
/// once at <c>Schedule(...)</c>, and the fire handler answers only "again or
/// done".
/// </summary>
public partial class CountdownCommandHandler : EdictCommandHandler<CountdownState>
{
    public Task<EdictCommandResult> HandleAsync(StartCountdownCommand command)
    {
        if (State.Remaining > 0)
        {
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("countdown_already_started", "This countdown is already running.")]));
        }

        State.CountdownId = command.CountdownId;
        State.Remaining = command.From;
        Schedule(new CountdownTick(), every: TimeSpan.FromMinutes(1));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictScheduleResult> HandleAsync(CountdownTick message)
    {
        State.Remaining--;
        Raise(new CountdownTickedEvent(State.CountdownId, State.Remaining));

        if (State.Remaining <= 0)
        {
            Raise(new CountdownFinishedEvent(State.CountdownId));
            return Complete();
        }

        return Continue();
    }
}
