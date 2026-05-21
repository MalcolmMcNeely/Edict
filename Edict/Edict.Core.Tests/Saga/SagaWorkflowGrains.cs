using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Commands;
using Edict.Core.Sagas;

using MessagePack;

using Orleans;

namespace Edict.Core.Tests.Saga;

// End-to-end saga fixture (ADR 0020): an event on the SagaWorkflow stream drives
// WorkflowSaga, which records durable Progress and dispatches exactly one
// SagaTrackerCommand. The ring slot, Progress, and the SendCommand effect commit
// in the one grain-document write, then the inline drain routes the command.

[EdictStream("SagaWorkflow")]
public sealed partial record SagaTriggerEvent(Guid WorkflowId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

[GenerateSerializer]
[Alias("Edict.Core.Tests.Saga.WorkflowProgress")]
public sealed class WorkflowProgress : IEdictPersistedState
{
    [Id(0)]
    public int Handled { get; set; }
}

[GenerateSerializer]
[Alias("Edict.Core.Tests.Saga.TrackerState")]
public sealed class TrackerState : IEdictPersistedState
{
    [Id(0)]
    public int Received { get; set; }

    [Id(1)]
    public Guid LastWorkflowId { get; set; }
}

// Hand-written probes (Orleans codegen sees these, unlike the Edict-generated
// grain interface — ADR 0006).
public interface ISagaProgressProbe : IGrainWithGuidKey
{
    Task<int> GetHandledAsync();
}

public interface ISagaTrackerProbe : IGrainWithGuidKey
{
    Task<int> GetReceivedAsync();
    Task<Guid> GetLastWorkflowIdAsync();
}

public partial class WorkflowSaga : EdictSaga<WorkflowProgress>, ISagaProgressProbe
{
    public Task Handle(SagaTriggerEvent evt)
    {
        Progress.Handled++;
        Dispatch(new SagaTrackerCommand(evt.WorkflowId));
        return Task.CompletedTask;
    }

    public Task<int> GetHandledAsync() => Task.FromResult(Progress.Handled);
}

public partial class SagaTrackerCommandHandler : EdictCommandHandler<TrackerState>, ISagaTrackerProbe
{
    public Task<EdictCommandResult> Handle(SagaTrackerCommand command)
    {
        State.Received++;
        State.LastWorkflowId = command.WorkflowId;
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<int> GetReceivedAsync() => Task.FromResult(State.Received);

    public Task<Guid> GetLastWorkflowIdAsync() => Task.FromResult(State.LastWorkflowId);
}
