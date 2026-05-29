using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Commands;
using Edict.Core.Sagas;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Tests.Conformance.Sagas;

[EdictStream("ConformanceSagaWorkflow")]
public sealed partial record SagaTriggerEvent(Guid WorkflowId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

public sealed partial record SagaTrackerCommand(Guid WorkflowId) : EdictCommand
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.Sagas.WorkflowProgress")]
public sealed class WorkflowProgress : IEdictPersistedState
{
    [Id(0)]
    public int Handled { get; set; }
}

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.Sagas.TrackerState")]
public sealed class TrackerState : IEdictPersistedState
{
    [Id(0)]
    public int Received { get; set; }

    [Id(1)]
    public Guid LastWorkflowId { get; set; }
}

// Hand-written probes (Orleans codegen sees these, unlike the Edict-generated
// grain interface).
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
    public Task Handle(SagaTriggerEvent edictEvent)
    {
        Progress.Handled++;
        Dispatch(new SagaTrackerCommand(edictEvent.WorkflowId));
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

// Pushes an event directly onto the saga's stream so the test can inject
// a SagaTriggerEvent with a known EventId without going through a command.
public interface ISagaEventPublisher : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent edictEvent);
}

public sealed class SagaEventPublisher : Grain, ISagaEventPublisher
{
    public Task PublishAsync(EdictEvent edictEvent)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("ConformanceSagaWorkflow", this.GetPrimaryKey()));
        return stream.OnNextAsync(edictEvent);
    }
}

static class SagaWaiters
{
    public static async Task WaitForReceivedAsync(
        ISagaTrackerProbe tracker, int expectedCount = 1, int timeoutSeconds = 15)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await tracker.GetReceivedAsync() >= expectedCount)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
