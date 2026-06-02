using System.Collections.Concurrent;

using Edict.Contracts.Commands;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.Sagas;
using Edict.Core.Outbox;
using Edict.Core.Sagas;

using Orleans;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Tests.Saga;

[EdictStream("SagaLifecycleWorkflow")]
public sealed partial record LifecycleTriggerEvent(Guid WorkflowId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

[EdictStream("SagaLifecycleWorkflow")]
public sealed partial record LifecycleFinishEvent(Guid WorkflowId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

// On the same stream as the trigger/finish events but handled by no saga in this
// workflow — the foreign event type a saga receives off a shared domain stream
// and must ignore, terminal or not.
[EdictStream("SagaLifecycleWorkflow")]
public sealed partial record LifecycleUnrelatedEvent(Guid WorkflowId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

[GenerateSerializer]
[Alias("Edict.Core.Tests.Saga.LifecycleProgress")]
public sealed class LifecycleProgress : IEdictPersistedState
{
    [Id(0)]
    public int Handled { get; set; }
}

// Probe surface Orleans codegen sees (unlike the Edict-generated grain
// interface), exposing the persisted lifecycle slot and a deterministic
// cap-fire trigger so the in-memory test never waits on Orleans' reminder floor.
public interface ISagaLifecycleProbe : IGrainWithGuidKey
{
    // Drives an event through the in-process IEdictEventConsumer seam so a test
    // delivers deterministically without the memory-stream pulling agent.
    Task DeliverAsync(EdictEvent edictEvent);
    Task<int> GetHandledAsync();
    Task<SagaLifecycleState?> GetLifecycleStateAsync();
    Task<DateTimeOffset?> GetDeadlineAsync();
    Task FireCapAsync();
    Task RequestDeactivationAsync();
}

// Default cap (no [EdictSagaTimeout]) so it inherits EdictSagaOptions.DefaultTimeout.
// Handles two event types: the trigger keeps the saga live; the finish calls
// Complete() to terminalise it.
public partial class DefaultCapSaga : EdictSaga<LifecycleProgress>, ISagaLifecycleProbe
{
    public Task HandleAsync(LifecycleTriggerEvent edictEvent)
    {
        Progress.Handled++;
        return Task.CompletedTask;
    }

    public Task HandleAsync(LifecycleFinishEvent edictEvent)
    {
        Progress.Handled++;
        Complete();
        return Task.CompletedTask;
    }

    public Task DeliverAsync(EdictEvent edictEvent) => OnEdictEventAsync(edictEvent);
    public Task<int> GetHandledAsync() => Task.FromResult(Progress.Handled);
    public Task<SagaLifecycleState?> GetLifecycleStateAsync() => Task.FromResult(State.Saga?.State);
    public Task<DateTimeOffset?> GetDeadlineAsync() => Task.FromResult(State.Saga?.DeadlineAt);
    public Task FireCapAsync() => ReceiveCapReminderAsync();
    public Task RequestDeactivationAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}

// Explicit short finite cap, used for the arming and fired-cap-dead-letters
// scenarios.
[EdictSagaTimeout("00:01:00")]
public partial class CappedSaga : EdictSaga<LifecycleProgress>, ISagaLifecycleProbe
{
    public Task HandleAsync(LifecycleTriggerEvent edictEvent)
    {
        Progress.Handled++;
        return Task.CompletedTask;
    }

    public Task DeliverAsync(EdictEvent edictEvent) => OnEdictEventAsync(edictEvent);
    public Task<int> GetHandledAsync() => Task.FromResult(Progress.Handled);
    public Task<SagaLifecycleState?> GetLifecycleStateAsync() => Task.FromResult(State.Saga?.State);
    public Task<DateTimeOffset?> GetDeadlineAsync() => Task.FromResult(State.Saga?.DeadlineAt);
    public Task FireCapAsync() => ReceiveCapReminderAsync();
    public Task RequestDeactivationAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}

// Explicit short finite cap with an OnSagaTimeoutAsync override: the fired cap
// runs compensation (mutates Progress, dispatches one compensating Command)
// instead of dead-lettering.
[EdictSagaTimeout("00:01:00")]
public partial class CompensatingSaga : EdictSaga<LifecycleProgress>, ISagaLifecycleProbe
{
    public Task HandleAsync(LifecycleTriggerEvent edictEvent)
    {
        Progress.Handled++;
        return Task.CompletedTask;
    }

    protected override Task OnSagaTimeoutAsync()
    {
        // A sentinel the test reads back to prove the hook saw and mutated the
        // durable Progress, and one compensating Command carrying the saga key.
        Progress.Handled = -1;
        Dispatch(new SagaTrackerCommand(this.GetPrimaryKey()));
        return Task.CompletedTask;
    }

    public Task DeliverAsync(EdictEvent edictEvent) => OnEdictEventAsync(edictEvent);
    public Task<int> GetHandledAsync() => Task.FromResult(Progress.Handled);
    public Task<SagaLifecycleState?> GetLifecycleStateAsync() => Task.FromResult(State.Saga?.State);
    public Task<DateTimeOffset?> GetDeadlineAsync() => Task.FromResult(State.Saga?.DeadlineAt);
    public Task FireCapAsync() => ReceiveCapReminderAsync();
    public Task RequestDeactivationAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}

// Replaces the real PublishEventExecutor in the lifecycle cluster so the
// EdictDeadLetterRaised the saga stages is captured directly — a deterministic,
// table-free observation of the dead-letter the lifecycle paths emit.
sealed class CapturingPublishEventExecutor(Serializer serializer) : IOutboxEffectExecutor
{
    public static readonly ConcurrentQueue<EdictDeadLetterRaised> Captured = new();

    public static void Reset() => Captured.Clear();

    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    public Task<OutboxEntry?> ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch,
        Type? consumerType,
        EdictEvent? liveWireEvent)
    {
        var edictEvent = liveWireEvent ?? serializer.Deserialize<EdictEvent>(entry.Payload);
        if (edictEvent is EdictDeadLetterRaised raised)
        {
            Captured.Enqueue(raised);
        }
        return Task.FromResult<OutboxEntry?>(null);
    }
}

// Replaces the real SendCommandExecutor in the lifecycle cluster so the
// compensating Command a fired cap dispatches is captured directly, without
// wiring a real IEdictSender — the durable observation of the compensation path.
sealed class CapturingSendCommandExecutor(Serializer serializer) : IOutboxEffectExecutor
{
    public static readonly ConcurrentQueue<EdictCommand> Captured = new();

    public static void Reset() => Captured.Clear();

    public OutboxEffectKind Kind => OutboxEffectKind.SendCommand;

    public Task<OutboxEntry?> ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch,
        Type? consumerType,
        EdictEvent? liveWireEvent)
    {
        Captured.Enqueue(serializer.Deserialize<EdictCommand>(entry.Payload));
        return Task.FromResult<OutboxEntry?>(null);
    }
}
