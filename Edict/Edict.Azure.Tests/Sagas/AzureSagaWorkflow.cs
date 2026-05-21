using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Commands;
using Edict.Core.Sagas;

using MessagePack;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Azure.Tests.Sagas;

// End-to-end saga fixture (ADR 0020) lifted from Edict.Core.Tests/Saga so the
// proof runs on Azurite via Testcontainers (ADR 0029): an event on the
// AzureSagaWorkflow stream drives AzureWorkflowSaga, which records durable
// Progress and dispatches exactly one AzureSagaTrackerCommand. The ring slot,
// Progress, and the SendCommand effect commit in the one grain-document write,
// then the inline drain routes the command.

[EdictStream("AzureSagaWorkflow")]
public sealed partial record AzureSagaTriggerEvent(Guid WorkflowId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

public sealed partial record AzureSagaTrackerCommand(Guid WorkflowId) : EdictCommand
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

[GenerateSerializer]
[Alias("Edict.Azure.Tests.Sagas.AzureWorkflowProgress")]
public sealed class AzureWorkflowProgress : IEdictPersistedState
{
    [Id(0)]
    public int Handled { get; set; }
}

[GenerateSerializer]
[Alias("Edict.Azure.Tests.Sagas.AzureTrackerState")]
public sealed class AzureTrackerState : IEdictPersistedState
{
    [Id(0)]
    public int Received { get; set; }

    [Id(1)]
    public Guid LastWorkflowId { get; set; }
}

// Hand-written probes (Orleans codegen sees these, unlike the Edict-generated
// grain interface — ADR 0006).
public interface IAzureSagaProgressProbe : IGrainWithGuidKey
{
    Task<int> GetHandledAsync();
}

public interface IAzureSagaTrackerProbe : IGrainWithGuidKey
{
    Task<int> GetReceivedAsync();
    Task<Guid> GetLastWorkflowIdAsync();
}

public partial class AzureWorkflowSaga : EdictSaga<AzureWorkflowProgress>, IAzureSagaProgressProbe
{
    public Task Handle(AzureSagaTriggerEvent evt)
    {
        Progress.Handled++;
        Dispatch(new AzureSagaTrackerCommand(evt.WorkflowId));
        return Task.CompletedTask;
    }

    public Task<int> GetHandledAsync() => Task.FromResult(Progress.Handled);
}

public partial class AzureSagaTrackerCommandHandler : EdictCommandHandler<AzureTrackerState>, IAzureSagaTrackerProbe
{
    public Task<EdictCommandResult> Handle(AzureSagaTrackerCommand command)
    {
        State.Received++;
        State.LastWorkflowId = command.WorkflowId;
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<int> GetReceivedAsync() => Task.FromResult(State.Received);

    public Task<Guid> GetLastWorkflowIdAsync() => Task.FromResult(State.LastWorkflowId);
}

// Publisher: pushes an event directly onto the saga's stream so the test
// can inject a SagaTriggerEvent with a known EventId without going through
// a command. Mirrors AzureEmailEventPublisher in the EventHandler suite.
public interface IAzureSagaEventPublisher : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent evt);
}

public sealed class AzureSagaEventPublisher : Grain, IAzureSagaEventPublisher
{
    public Task PublishAsync(EdictEvent evt)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("AzureSagaWorkflow", this.GetPrimaryKey()));
        return stream.OnNextAsync(evt);
    }
}

static class AzureSagaWaiters
{
    public static async Task WaitForReceivedAsync(
        IAzureSagaTrackerProbe tracker, int expectedCount = 1, int timeoutSeconds = 15)
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
