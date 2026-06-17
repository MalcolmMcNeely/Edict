using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.Sagas;
using Edict.Core.Commands;
using Edict.Core.Sagas;

using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Tests.Conformance.Sagas;

[EdictStream("ConformanceSagaTimeoutWorkflow")]
public sealed partial record TimeoutTriggerEvent(Guid WorkflowId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

[EdictStream("ConformanceSagaTimeoutThrowingWorkflow")]
public sealed partial record TimeoutThrowingTriggerEvent(Guid WorkflowId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

[EdictStream("ConformanceSagaTimeoutDefaultHookWorkflow")]
public sealed partial record TimeoutDefaultHookTriggerEvent(Guid WorkflowId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

[EdictStream("ConformanceSagaTerminalWorkflow")]
public sealed partial record TerminalTriggerEvent(Guid WorkflowId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

[EdictStream("ConformanceSagaTerminalWorkflow")]
public sealed partial record TerminalFinishEvent(Guid WorkflowId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

public sealed partial record TimeoutCompensationCommand(Guid WorkflowId) : EdictCommand
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.Sagas.TimeoutSagaProgress")]
public sealed class TimeoutSagaProgress : IEdictPersistedState
{
    [Id(0)]
    public int Handled { get; set; }
}

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.Sagas.CompensationTrackerState")]
public sealed class CompensationTrackerState : IEdictPersistedState
{
    [Id(0)]
    public int Received { get; set; }

    [Id(1)]
    public Guid LastWorkflowId { get; set; }
}

// Hand-written probes (Orleans codegen sees these, unlike the Edict-generated
// grain interface). FireCapAsync drives the cap deterministically — the real
// Orleans reminder floors at one minute, too long for a conformance run.
public interface ITimeoutSagaProbe : IGrainWithGuidKey
{
    Task FireCapAsync();
    Task<int> GetHandledAsync();
    Task<int> GetPendingOutboxCountAsync();
}

public interface IThrowingTimeoutSagaProbe : IGrainWithGuidKey
{
    Task FireCapAsync();
    Task<int> GetHandledAsync();
    Task<int> GetPendingOutboxCountAsync();
}

public interface IDefaultHookTimeoutSagaProbe : IGrainWithGuidKey
{
    Task FireCapAsync();
    Task<int> GetHandledAsync();
}

public interface ITerminalSagaProbe : IGrainWithGuidKey
{
    Task<int> GetHandledAsync();
}

public interface ICompensationTrackerProbe : IGrainWithGuidKey
{
    Task<int> GetReceivedAsync();
    Task<Guid> GetLastWorkflowIdAsync();
}

// A finite cap with an OnSagaTimeoutAsync override: the fired cap dispatches one
// compensating Command. The one-second cap lets a conformance run arm, advance past
// it on the virtual clock, then fire it through the probe.
[EdictSagaTimeout("00:00:01")]
public partial class TimeoutCompensatingSaga : EdictSaga<TimeoutSagaProgress>, ITimeoutSagaProbe
{
    Task HandleAsync(TimeoutTriggerEvent edictEvent)
    {
        Progress.Handled++;
        return Task.CompletedTask;
    }

    protected override Task OnSagaTimeoutAsync()
    {
        Dispatch(new TimeoutCompensationCommand(this.GetPrimaryKey()));
        return Task.CompletedTask;
    }

    public Task FireCapAsync() => ReceiveCapReminderAsync();
    public Task<int> GetHandledAsync() => Task.FromResult(Progress.Handled);
    public Task<int> GetPendingOutboxCountAsync() => Task.FromResult(OutboxStateForProbe.Pending.Count);
}

// A finite cap whose OnSagaTimeoutAsync override throws — the consumer-bug shape
// the cap handler must contain. A fired cap runs the throwing hook; the engine
// rolls back the half-applied compensation, terminalises the saga, and
// dead-letters as a ConsumerBug rather than re-throwing on every reminder tick.
// The one-second cap lets a conformance run arm, advance past it on the virtual
// clock, then fire it through the probe.
[EdictSagaTimeout("00:00:01")]
public partial class TimeoutThrowingSaga : EdictSaga<TimeoutSagaProgress>, IThrowingTimeoutSagaProbe
{
    Task HandleAsync(TimeoutThrowingTriggerEvent edictEvent)
    {
        Progress.Handled++;
        return Task.CompletedTask;
    }

    protected override Task OnSagaTimeoutAsync() =>
        throw new InvalidOperationException("Simulated compensation fault from OnSagaTimeoutAsync.");

    public Task FireCapAsync() => ReceiveCapReminderAsync();
    public Task<int> GetHandledAsync() => Task.FromResult(Progress.Handled);
    public Task<int> GetPendingOutboxCountAsync() => Task.FromResult(OutboxStateForProbe.Pending.Count);
}

// A finite cap with no OnSagaTimeoutAsync override: a fired cap routes to the
// base's default hook, which dead-letters the timeout rather than compensating.
// The one-second cap lets a conformance run arm, advance past it on the virtual
// clock, then fire it through the probe.
[EdictSagaTimeout("00:00:01")]
public partial class TimeoutDefaultHookSaga : EdictSaga<TimeoutSagaProgress>, IDefaultHookTimeoutSagaProbe
{
    Task HandleAsync(TimeoutDefaultHookTriggerEvent edictEvent)
    {
        Progress.Handled++;
        return Task.CompletedTask;
    }

    public Task FireCapAsync() => ReceiveCapReminderAsync();
    public Task<int> GetHandledAsync() => Task.FromResult(Progress.Handled);
}

// Default cap; terminalises via Complete() on the finish event. Used for the
// terminal-guard scenario (a genuinely-new Event after Complete dead-letters).
public partial class TimeoutTerminalSaga : EdictSaga<TimeoutSagaProgress>, ITerminalSagaProbe
{
    Task HandleAsync(TerminalTriggerEvent edictEvent)
    {
        Progress.Handled++;
        return Task.CompletedTask;
    }

    Task HandleAsync(TerminalFinishEvent edictEvent)
    {
        Progress.Handled++;
        Complete();
        return Task.CompletedTask;
    }

    public Task<int> GetHandledAsync() => Task.FromResult(Progress.Handled);
}

public partial class TimeoutCompensationCommandHandler : EdictCommandHandler<CompensationTrackerState>, ICompensationTrackerProbe
{
    Task<EdictCommandResult> HandleAsync(TimeoutCompensationCommand command)
    {
        State.Received++;
        State.LastWorkflowId = command.WorkflowId;
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<int> GetReceivedAsync() => Task.FromResult(State.Received);
    public Task<Guid> GetLastWorkflowIdAsync() => Task.FromResult(State.LastWorkflowId);
}

// Pushes events onto a saga's stream so a test can inject a known event. The
// inline path pushes the event itself; the oversized path stages the body in the
// claim-check store and pushes a pointer-bearing envelope, so the receiver takes
// the deferred (InvokeHandler) branch regardless of the configured threshold.
public interface ISagaTimeoutPublisher : IGrainWithGuidKey
{
    Task PublishAsync(string streamNamespace, EdictEvent edictEvent);
    Task PublishOversizedAsync(string streamNamespace, EdictEvent innerEvent);
}

public sealed class SagaTimeoutPublisher : Grain, ISagaTimeoutPublisher
{
    public Task PublishAsync(string streamNamespace, EdictEvent edictEvent) =>
        StreamFor(streamNamespace).OnNextAsync(edictEvent);

    public async Task PublishOversizedAsync(string streamNamespace, EdictEvent innerEvent)
    {
        var serializer = ServiceProvider.GetRequiredService<Serializer>();
        var store = ServiceProvider.GetRequiredService<IEdictClaimCheckStore>();

        // Single-identity model: the parked body is keyed by the inner event's
        // own EventId, and the pointer envelope carries that same id.
        var stamped = innerEvent.EventId == Guid.Empty
            ? innerEvent with { EventId = Guid.NewGuid() }
            : innerEvent;
        await store.PutAsync(
            stamped.Tenant, stamped.EventId, serializer.SerializeToArray<EdictEvent>(stamped), CancellationToken.None);

        var envelope = new EdictEventEnvelope(null, stamped.EventId)
        {
            OccurredAt = DateTimeOffset.UtcNow,
            InnerEventStreamName = streamNamespace,
            InnerEventRouteKey = this.GetPrimaryKey().ToString("N"),
            Tenant = stamped.Tenant,
        };

        await StreamFor(streamNamespace).OnNextAsync(envelope);
    }

    IAsyncStream<EdictEvent> StreamFor(string streamNamespace) =>
        this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create(streamNamespace, this.GetPrimaryKey()));
}

static class SagaTimeoutWaiters
{
    public static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutSeconds = 20)
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

    // On a virtual-clock fixture the reference stream's pulling agent polls on that
    // clock, so a queued message never delivers until the clock moves. This nudges
    // the clock forward in small steps to pump delivery, polling the condition
    // between nudges; the nudge drives the agent, the wall-clock delay lets the
    // resulting async grain delivery settle. The cap itself stays driven by an
    // explicit AdvanceClock + FireCapAsync, never by this pump.
    public static async Task PumpUntilAsync(Action<TimeSpan> advanceClock, Func<Task<bool>> condition, int timeoutSeconds = 20)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }
            advanceClock(TimeSpan.FromMilliseconds(250));
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
