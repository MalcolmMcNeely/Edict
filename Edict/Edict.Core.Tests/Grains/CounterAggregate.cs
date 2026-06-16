using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.Telemetry;
using Edict.Core.Commands;
using Edict.Core.Outbox;

using MessagePack;

using Orleans;
using Orleans.Runtime;

namespace Edict.Core.Tests.Grains;

[GenerateSerializer]
[Alias("Edict.Core.Tests.Grains.CounterState")]
public sealed class CounterState : IEdictPersistedState
{
    [Id(0)]
    public int Count { get; set; }
}

public sealed partial record IncrementCounterCommand(Guid CounterId) : EdictCommand
{
    [EdictRouteKey]
    public Guid CounterId { get; init; } = CounterId;
}

public sealed partial record BatchIncrementCounterCommand(Guid CounterId, int Times) : EdictCommand
{
    [EdictRouteKey]
    public Guid CounterId { get; init; } = CounterId;

    public int Times { get; init; } = Times;
}

// Always fails its validator, so a test can exercise the validator-rejection
// audit-capture path (the one that otherwise writes nothing).
public sealed partial record RejectByValidatorCommand(Guid CounterId) : EdictCommand
{
    [EdictRouteKey]
    public Guid CounterId { get; init; } = CounterId;
}

[EdictStream("Counters")]
public sealed partial record CounterIncrementedEvent(Guid CounterId, int NewCount) : EdictEvent
{
    [EdictRouteKey]
    public Guid CounterId { get; init; } = CounterId;

    public int NewCount { get; init; } = NewCount;
}

// Hand-written probe interface (Orleans' codegen can see this, unlike the
// Edict-generated grain interface), so a test can read the
// framework-owned State and force deactivation to prove it persisted.
public interface ICounterProbe : IGrainWithGuidKey
{
    Task<int> GetCountAsync();
    Task DeactivateAsync();
    Task ForceDrainViaReminderAsync();
    Task<int> GetPendingOutboxCountAsync();
    Task<bool> HasDrainReminderAsync();
    Task ForceAuditDrainViaReminderAsync();
    Task<int> GetPendingAuditCountAsync();
    Task<bool> HasAuditDrainReminderAsync();
}

public partial class CounterAggregate : EdictCommandHandler<CounterState>, ICounterProbe
{
    Task<EdictCommandResult> HandleAsync(IncrementCounterCommand command)
    {
        State.Count++;
        Raise(new CounterIncrementedEvent(command.CounterId, State.Count));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    // Raises several events in one command so a test can prove the inline
    // drain publishes them FIFO (per-aggregate causal order).
    Task<EdictCommandResult> HandleAsync(BatchIncrementCounterCommand command)
    {
        for (var i = 0; i < command.Times; i++)
        {
            State.Count++;
            Raise(new CounterIncrementedEvent(command.CounterId, State.Count));
        }

        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    // Never reached: the validator rejects first. Present so the generated
    // dispatch spine has an arm to route RejectByValidatorCommand to.
    Task<EdictCommandResult> HandleAsync(RejectByValidatorCommand command) =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

    public Task<int> GetCountAsync() => Task.FromResult(State.Count);

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    // Exercises the real Reminder recovery path (ReceiveReminder → drain)
    // deterministically, without waiting out Orleans' one-minute floor.
    public Task ForceDrainViaReminderAsync() =>
        ReceiveReminder("edict-outbox-drain", new TickStatus());

    public Task<int> GetPendingOutboxCountAsync() =>
        Task.FromResult(OutboxStateForProbe.Pending.Count);

    public async Task<bool> HasDrainReminderAsync() =>
        await this.GetReminder("edict-outbox-drain") is not null;

    public Task ForceAuditDrainViaReminderAsync() =>
        ReceiveReminder("edict-audit-drain", new TickStatus());

    public Task<int> GetPendingAuditCountAsync() =>
        Task.FromResult(AuditStateForProbe?.Pending.Count ?? 0);

    public async Task<bool> HasAuditDrainReminderAsync() =>
        await this.GetReminder("edict-audit-drain") is not null;
}
