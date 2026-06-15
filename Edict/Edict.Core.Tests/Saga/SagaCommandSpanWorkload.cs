using Edict.Contracts.Audit;
using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Commands;
using Edict.Core.Sagas;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Tests.Saga;

[EdictStream("SpanSagaWorkflow")]
public sealed partial record SpanSagaTriggerEvent(Guid WorkflowId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

public sealed partial record SpanTrackerCommand(Guid WorkflowId) : EdictCommand
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

[EdictStream("SpanTrackerWorkflow")]
public sealed partial record SpanTrackerRaisedEvent(Guid WorkflowId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

[GenerateSerializer]
[Alias("Edict.Core.Tests.Saga.SpanWorkflowProgress")]
public sealed class SpanWorkflowProgress : IEdictPersistedState
{
    [Id(0)]
    public int Handled { get; set; }
}

[GenerateSerializer]
[Alias("Edict.Core.Tests.Saga.SpanTrackerState")]
public sealed class SpanTrackerState : IEdictPersistedState
{
    [Id(0)]
    public int Received { get; set; }

    [Id(1)]
    public Guid LastCorrelationId { get; set; }

    [Id(2)]
    public EdictPrincipal? LastPrincipal { get; set; }
}

// Hand-written probe (Orleans codegen sees this, unlike the Edict-generated
// grain interface).
public interface ISpanTrackerProbe : IGrainWithGuidKey
{
    Task<int> GetReceivedAsync();
    Task<Guid> GetLastCorrelationIdAsync();
    Task<EdictPrincipal?> GetLastPrincipalAsync();
}

public partial class SpanWorkflowSaga : EdictSaga<SpanWorkflowProgress>
{
    Task HandleAsync(SpanSagaTriggerEvent edictEvent)
    {
        Progress.Handled++;
        Dispatch(new SpanTrackerCommand(edictEvent.WorkflowId));
        return Task.CompletedTask;
    }
}

public partial class SpanTrackerCommandHandler : EdictCommandHandler<SpanTrackerState>, ISpanTrackerProbe
{
    Task<EdictCommandResult> HandleAsync(SpanTrackerCommand command)
    {
        State.Received++;
        State.LastCorrelationId = command.CorrelationId;
        State.LastPrincipal = command.Principal;
        Raise(new SpanTrackerRaisedEvent(command.WorkflowId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<int> GetReceivedAsync() => Task.FromResult(State.Received);

    public Task<Guid> GetLastCorrelationIdAsync() => Task.FromResult(State.LastCorrelationId);

    public Task<EdictPrincipal?> GetLastPrincipalAsync() => Task.FromResult(State.LastPrincipal);
}

// Pushes an event directly onto the saga's stream so the test can inject a
// SpanSagaTriggerEvent with a known EventId without going through a command.
public interface ISpanSagaEventPublisher : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent edictEvent);
}

public sealed class SpanSagaEventPublisher : Grain, ISpanSagaEventPublisher
{
    public Task PublishAsync(EdictEvent edictEvent)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("SpanSagaWorkflow", this.GetPrimaryKey()));
        return stream.OnNextAsync(edictEvent);
    }
}

static class SpanSagaWaiters
{
    public static async Task WaitForReceivedAsync(
        ISpanTrackerProbe tracker, int expectedCount = 1, int timeoutSeconds = 15)
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
