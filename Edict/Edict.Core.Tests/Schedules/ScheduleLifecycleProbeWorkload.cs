using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.Schedules;
using Edict.Core.Commands;
using Edict.Core.Schedules;

using MessagePack;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Tests.Schedules;

public enum ScheduleProbeBehavior
{
    ContinueNoEvent,
    ContinueRaiseEvent,
    CompleteNoEvent,
    Throw,
}

// Whether the probe writes an OnScheduleTimeoutAsync compensation hook
// (Compensate) or leaves the fired cap to dead-letter (DeadLetter).
public enum ScheduleProbeTimeoutBehavior
{
    DeadLetter,
    Compensate,
    CompensateThrows,
}

[GenerateSerializer]
[Alias("Edict.Core.Tests.Schedules.ScheduleProbeState")]
public sealed class ScheduleProbeState : IEdictPersistedState
{
    [Id(0)]
    public int Counter { get; set; }

    [Id(1)]
    public ScheduleProbeBehavior Behavior { get; set; }

    [Id(2)]
    public ScheduleProbeTimeoutBehavior TimeoutBehavior { get; set; }

    [Id(3)]
    public int TimeoutCounter { get; set; }
}

// Constructed inside the grain and passed straight to ValidateAndHandleAsync, so
// it never crosses a grain boundary. Carries a route key only to satisfy EDICT003.
public sealed partial record ScheduleProbeStartCommand(Guid Key) : EdictCommand
{
    [EdictRouteKey]
    public Guid Key { get; init; } = Key;
}

public sealed partial record ScheduleTickMessage : EdictScheduleMessage;

[EdictStream("ScheduleProbe")]
public sealed partial record ScheduleTickedEvent(Guid Key, int Counter) : EdictEvent
{
    [EdictRouteKey]
    public Guid Key { get; init; } = Key;

    public int Counter { get; init; } = Counter;
}

// Hand-written probe interface (Orleans codegen sees this, unlike the
// Edict-generated grain interface) so a test can start a schedule, fire it, and
// read back its observable effects. Extends IEdictScheduleFireable so the one
// reference both starts and fires.
public interface IScheduleProbe : IEdictScheduleFireable
{
    Task StartAsync(TimeSpan period, ScheduleProbeBehavior behavior);
    Task StartWithTimeoutAsync(TimeSpan period, TimeSpan timeout, ScheduleProbeTimeoutBehavior timeoutBehavior);
    Task<int> GetCounterAsync();
    Task<int> GetTimeoutCounterAsync();
    Task<int> GetPendingOutboxCountAsync();
    Task<Guid> GetActivationIdAsync();
    Task DeactivateAsync();
}

// Hand-writes both dispatch overrides so the schedule fire lifecycle under test is
// the Edict.Core method, not a generated spine.
public partial class ScheduleProbe : EdictCommandHandler<ScheduleProbeState>, IScheduleProbe
{
    Guid _activationId;

    public override Task<EdictCommandResult> DispatchAsync(EdictCommand command) =>
        throw new NotSupportedException("ScheduleProbe drives ValidateAndHandleAsync directly.");

    protected override Task<EdictScheduleResult> DispatchScheduleFireAsync(EdictScheduleMessage message) =>
        message switch
        {
            ScheduleTickMessage tick => HandleAsync(tick),
            _ => throw new EdictUnroutableScheduleMessageException(message.GetType()),
        };

    // Hand-written timeout dispatch: DeadLetter leaves the cap unhandled (base
    // default), Compensate runs the hook and returns true, CompensateThrows models
    // a compensation that faults mid-hook.
    protected override async Task<bool> DispatchScheduleTimeoutAsync(EdictScheduleMessage message)
    {
        if (State.TimeoutBehavior == ScheduleProbeTimeoutBehavior.DeadLetter)
        {
            return false;
        }

        switch (message)
        {
            case ScheduleTickMessage:
                await OnScheduleTimeoutAsync();
                return true;
            default:
                return false;
        }
    }

    Task OnScheduleTimeoutAsync()
    {
        State.TimeoutCounter++;
        if (State.TimeoutBehavior == ScheduleProbeTimeoutBehavior.CompensateThrows)
        {
            throw new InvalidOperationException("compensation hook threw after mutating State.");
        }
        return Task.CompletedTask;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _activationId = Guid.NewGuid();
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<Guid> GetActivationIdAsync() => Task.FromResult(_activationId);

    public Task StartAsync(TimeSpan period, ScheduleProbeBehavior behavior) =>
        ValidateAndHandleAsync(new ScheduleProbeStartCommand(this.GetPrimaryKey()), () =>
        {
            State.Behavior = behavior;
            Schedule(new ScheduleTickMessage(), period);
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
        });

    public Task StartWithTimeoutAsync(TimeSpan period, TimeSpan timeout, ScheduleProbeTimeoutBehavior timeoutBehavior) =>
        ValidateAndHandleAsync(new ScheduleProbeStartCommand(this.GetPrimaryKey()), () =>
        {
            State.Behavior = ScheduleProbeBehavior.ContinueNoEvent;
            State.TimeoutBehavior = timeoutBehavior;
            Schedule(new ScheduleTickMessage(), period, timeout);
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
        });

    Task<EdictScheduleResult> HandleAsync(ScheduleTickMessage message)
    {
        State.Counter++;

        return State.Behavior switch
        {
            ScheduleProbeBehavior.Throw => throw new InvalidOperationException("schedule fire threw after mutating State."),
            ScheduleProbeBehavior.CompleteNoEvent => Complete(),
            ScheduleProbeBehavior.ContinueRaiseEvent => RaiseThenContinue(),
            _ => Continue(),
        };
    }

    Task<EdictScheduleResult> RaiseThenContinue()
    {
        Raise(new ScheduleTickedEvent(this.GetPrimaryKey(), State.Counter));
        return Continue();
    }

    public Task<int> GetCounterAsync() => Task.FromResult(State.Counter);

    public Task<int> GetTimeoutCounterAsync() => Task.FromResult(State.TimeoutCounter);

    public Task<int> GetPendingOutboxCountAsync() => Task.FromResult(OutboxStateForProbe.Pending.Count);

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}
