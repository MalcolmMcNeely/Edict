using System.Reflection;

using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.Telemetry;
using Edict.Core.Commands;
using Edict.Core.Outbox;

using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Tests.Conformance.Outbox;

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.Outbox.CounterState")]
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

// A handler-path rejection, so an audit conformance scenario can capture an
// Accepted and a Rejected decision on the same aggregate's chain.
public sealed partial record RejectCounterCommand(Guid CounterId) : EdictCommand
{
    [EdictRouteKey]
    public Guid CounterId { get; init; } = CounterId;
}

[EdictStream("ConformanceCounters")]
public sealed partial record CounterIncrementedEvent(Guid CounterId, int NewCount) : EdictEvent
{
    [EdictRouteKey]
    public Guid CounterId { get; init; } = CounterId;

    public int NewCount { get; init; } = NewCount;
}

// Hand-written probe (Orleans codegen can't see the Edict-generated grain
// interface) so tests can read framework-owned State and drive the Reminder
// recovery path deterministically.
public interface ICounterProbe : IGrainWithGuidKey
{
    Task<int> GetCountAsync();
    Task<Guid> GetActivationIdAsync();
    Task DeactivateAsync();
    Task ForceDrainViaReminderAsync();
    Task<int> GetPendingOutboxCountAsync();
    Task<bool> HasDrainReminderAsync();

    // Stage a poisoned outbox entry that drives one of the DeadLetterPromoter
    // degrade-arm causes, so a real drain (via ForceDrainViaReminderAsync) reaches
    // the arm. Each stages and commits the entry; the scenario drives the drain.
    Task StageUnserialisableForensicBodyEntryAsync();
    Task StageUnsupportedKindEntryAsync();
    Task StageMissingRouteKeySendCommandEntryAsync();

    // Stage a valid SendCommand outbox effect that relays an increment to another
    // counter, so a real drain exercises the SendCommand executor's fault and
    // recovery path. The scenario drives the drain and reads the relayed target.
    Task StageSendCommandAsync(Guid targetCounterId);
}

public partial class CounterAggregate : EdictCommandHandler<CounterState>, ICounterProbe
{
    Guid _activationId;

    // Fresh per activation, so a scenario can poll until it changes to confirm
    // the framework deactivated a dirty activation after a write fault.
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _activationId = Guid.NewGuid();
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<Guid> GetActivationIdAsync() => Task.FromResult(_activationId);

    Task<EdictCommandResult> HandleAsync(IncrementCounterCommand command)
    {
        State.Count++;
        Raise(new CounterIncrementedEvent(command.CounterId, State.Count));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    Task<EdictCommandResult> HandleAsync(BatchIncrementCounterCommand command)
    {
        for (var i = 0; i < command.Times; i++)
        {
            State.Count++;
            Raise(new CounterIncrementedEvent(command.CounterId, State.Count));
        }
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    Task<EdictCommandResult> HandleAsync(RejectCounterCommand command) =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
            [new EdictRejectionReason("counter_rejected", "Rejected for the audit conformance test.")]));

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

    public Task StageUnserialisableForensicBodyEntryAsync()
    {
        var serializer = ServiceProvider.GetRequiredService<Serializer>();
        var payload = serializer.SerializeToArray<EdictEvent>(
            new UnserialisableForensicBodyEvent { RouteKey = this.GetPrimaryKey() });
        return StageAndCommitAsync(OutboxEffectKind.PublishEvent, payload);
    }

    public Task StageMissingRouteKeySendCommandEntryAsync()
    {
        var serializer = ServiceProvider.GetRequiredService<Serializer>();
        var payload = serializer.SerializeToArray<EdictCommand>(
            new MissingRouteKeyCommand { Id = this.GetPrimaryKey() });
        return StageAndCommitAsync(OutboxEffectKind.SendCommand, payload);
    }

    public Task StageUnsupportedKindEntryAsync() =>
        StageAndCommitAsync(UnsupportedKindFailingExecutor.Kind, []);

    public Task StageSendCommandAsync(Guid targetCounterId)
    {
        var serializer = ServiceProvider.GetRequiredService<Serializer>();
        var payload = serializer.SerializeToArray<EdictCommand>(new IncrementCounterCommand(targetCounterId));
        return StageAndCommitAsync(OutboxEffectKind.SendCommand, payload);
    }

    // Enqueues a hand-built entry directly onto the framework-owned outbox slot —
    // since no consumer path produces an unsupported-kind or route-key-less entry —
    // and commits it. The base Grain envelope is reached by reflection: the engine
    // shadows the inherited State with its own payload-typed State, and C#'s
    // protected-access rule blocks reading the base property through a base-typed
    // cast, so the test harness reads it reflectively rather than the framework
    // growing a staging seam for it. The next reminder drain runs the entry through
    // the real engine, exhausts its attempts against the failing executor wired for
    // its kind, and promotes it.
    async Task StageAndCommitAsync(OutboxEffectKind kind, byte[] payload)
    {
        var now = ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var entry = new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = kind,
            Payload = payload,
            NextAttemptUtc = now,
            EnqueuedAt = now,
        };

        var envelope = (GrainEnvelope<CounterState>)EnvelopeStateProperty.GetValue(this)!;
        envelope.Outbox = envelope.Outbox.Enqueue(entry);
        await WriteStateAsync();
    }

    static readonly PropertyInfo EnvelopeStateProperty =
        typeof(Grain<GrainEnvelope<CounterState>>).GetProperty(
            "State", BindingFlags.Instance | BindingFlags.NonPublic)!;
}

public interface ICounterEventCaptureGrain : IGrainWithGuidKey
{
    Task<IReadOnlyList<EdictEvent>> GetCapturedEventsAsync();
}

[ImplicitStreamSubscription("ConformanceCounters")]
public sealed class CounterEventCaptureGrain : Grain, ICounterEventCaptureGrain
{
    readonly List<EdictEvent> _events = [];

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("ConformanceCounters", this.GetPrimaryKey()));
        await stream.SubscribeAsync(
            (item, _) => { _events.Add(item); return Task.CompletedTask; },
            _ => Task.CompletedTask);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<IReadOnlyList<EdictEvent>> GetCapturedEventsAsync() =>
        Task.FromResult<IReadOnlyList<EdictEvent>>(_events.AsReadOnly());
}
