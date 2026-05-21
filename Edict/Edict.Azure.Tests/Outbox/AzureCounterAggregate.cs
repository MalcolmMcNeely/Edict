using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.Telemetry;
using Edict.Core.Commands;
using Edict.Core.Outbox;

using MessagePack;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Azure.Tests.Outbox;

// Stateful command-handler aggregate used by the outbox lift suite (issue #88).
// Mirrors Edict.Core.Tests' CounterAggregate but raises events on the
// AzureCounters stream so the Azurite-backed cluster can observe end-to-end
// {State, Outbox} commit + inline FIFO drain over the real Azure Queue
// transport.

[GenerateSerializer]
[Alias("Edict.Azure.Tests.Outbox.AzureCounterState")]
public sealed class AzureCounterState : IEdictPersistedState
{
    [Id(0)]
    public int Count { get; set; }
}

public sealed partial record AzureIncrementCounterCommand(Guid CounterId) : EdictCommand
{
    [EdictRouteKey]
    public Guid CounterId { get; init; } = CounterId;
}

public sealed partial record AzureBatchIncrementCounterCommand(Guid CounterId, int Times) : EdictCommand
{
    [EdictRouteKey]
    public Guid CounterId { get; init; } = CounterId;

    public int Times { get; init; } = Times;
}

[EdictStream("AzureCounters")]
public sealed partial record AzureCounterIncrementedEvent(Guid CounterId, int NewCount) : EdictEvent
{
    [EdictRouteKey]
    public Guid CounterId { get; init; } = CounterId;

    public int NewCount { get; init; } = NewCount;
}

// Hand-written probe interface (Orleans codegen cannot see the Edict-generated
// grain interface) so tests can read framework-owned State, force
// deactivation, and drive the deterministic Reminder recovery path.
public interface IAzureCounterProbe : IGrainWithGuidKey
{
    Task<int> GetCountAsync();
    Task DeactivateAsync();
    Task ForceDrainViaReminderAsync();
    Task<int> GetPendingOutboxCountAsync();
    Task<bool> HasDrainReminderAsync();
}

public partial class AzureCounterAggregate : EdictCommandHandler<AzureCounterState>, IAzureCounterProbe
{
    public Task<EdictCommandResult> Handle(AzureIncrementCounterCommand command)
    {
        State.Count++;
        Raise(new AzureCounterIncrementedEvent(command.CounterId, State.Count));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> Handle(AzureBatchIncrementCounterCommand command)
    {
        for (var i = 0; i < command.Times; i++)
        {
            State.Count++;
            Raise(new AzureCounterIncrementedEvent(command.CounterId, State.Count));
        }
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<int> GetCountAsync() => Task.FromResult(State.Count);

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public Task ForceDrainViaReminderAsync() =>
        ReceiveReminder("edict-outbox-drain", new TickStatus());

    public Task<int> GetPendingOutboxCountAsync() =>
        Task.FromResult(OutboxStateForProbe.Pending.Count);

    public async Task<bool> HasDrainReminderAsync() =>
        await this.GetReminder("edict-outbox-drain") is not null;
}

public interface IAzureCounterEventCaptureGrain : IGrainWithGuidKey
{
    Task<IReadOnlyList<EdictEvent>> GetCapturedEventsAsync();
}

[ImplicitStreamSubscription("AzureCounters")]
public sealed class AzureCounterEventCaptureGrain : Grain, IAzureCounterEventCaptureGrain
{
    readonly List<EdictEvent> _events = [];

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("AzureCounters", this.GetPrimaryKey()));
        await stream.SubscribeAsync(
            (item, _) => { _events.Add(item); return Task.CompletedTask; },
            _ => Task.CompletedTask);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<IReadOnlyList<EdictEvent>> GetCapturedEventsAsync() =>
        Task.FromResult<IReadOnlyList<EdictEvent>>(_events.AsReadOnly());
}
