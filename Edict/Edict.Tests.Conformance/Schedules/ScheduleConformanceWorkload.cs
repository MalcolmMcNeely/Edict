using Edict.Contracts.Commands;
using Edict.Contracts.Persistence;
using Edict.Contracts.Schedules;
using Edict.Core.Commands;

using Orleans;

namespace Edict.Tests.Conformance.Schedules;

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.Schedules.ScheduleConformanceState")]
public sealed class ScheduleConformanceState : IEdictPersistedState
{
    [Id(0)]
    public int TickCount { get; set; }

    [Id(1)]
    public int TimeoutCount { get; set; }
}

// Carries the cadence and cap so a scenario can pick a long period (so the real
// wall-clock grain timer never fires during the test) and drive firing purely by
// advancing the fixture's virtual clock past the due and deadline instants.
public sealed partial record ArmScheduleCommand(Guid Key, TimeSpan Period, TimeSpan? Timeout) : EdictCommand
{
    [EdictRouteKey]
    public Guid Key { get; init; } = Key;

    public TimeSpan Period { get; init; } = Period;

    public TimeSpan? Timeout { get; init; } = Timeout;
}

public sealed partial record ScheduleConformanceTick : EdictScheduleMessage;

// Hand-written probe (Orleans codegen can't see the Edict-generated grain
// interface) so a scenario can arm a schedule, force deactivation, observe the
// reactivation, and read the durable fire counts the catch-up-on-activation path
// left behind.
public interface IScheduleConformanceProbe : IGrainWithGuidKey
{
    Task<int> GetTickCountAsync();
    Task<int> GetTimeoutCountAsync();
    Task<Guid> GetActivationIdAsync();
    Task<DateTimeOffset?> PeekSoonestDueAsync();
    Task DeactivateAsync();
}

// A real generated aggregate (the source generator emits the command and schedule
// dispatch spines), so the scenario exercises the production fire lifecycle over
// the provider's real grain storage — not a hand-written shortcut.
public partial class ScheduleConformanceAggregate : EdictCommandHandler<ScheduleConformanceState>, IScheduleConformanceProbe
{
    Guid _activationId;

    // Fresh per activation so a scenario can poll until it changes to confirm the
    // grain genuinely deactivated and came back from durable storage.
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _activationId = Guid.NewGuid();
        await base.OnActivateAsync(cancellationToken);
    }

    Task<EdictCommandResult> HandleAsync(ArmScheduleCommand command)
    {
        Schedule(new ScheduleConformanceTick(), every: command.Period, timeout: command.Timeout);
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    Task<EdictScheduleResult> HandleAsync(ScheduleConformanceTick message)
    {
        State.TickCount++;
        return Continue();
    }

    Task OnScheduleTimeoutAsync(ScheduleConformanceTick message)
    {
        State.TimeoutCount++;
        return Task.CompletedTask;
    }

    public Task<int> GetTickCountAsync() => Task.FromResult(State.TickCount);

    public Task<int> GetTimeoutCountAsync() => Task.FromResult(State.TimeoutCount);

    public Task<Guid> GetActivationIdAsync() => Task.FromResult(_activationId);

    public Task<DateTimeOffset?> PeekSoonestDueAsync() => PeekSoonestScheduleDueAsync();

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}
