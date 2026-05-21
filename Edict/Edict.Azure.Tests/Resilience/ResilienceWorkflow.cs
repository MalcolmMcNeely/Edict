using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Commands;
using Edict.Core.Idempotency;
using Edict.Core.Sagas;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Azure.Tests.Resilience;

// Dedicated event/saga types for the transport-fault suite (ADR 0029 +
// issue #96). The resilience cluster has its own Azurite container so it can
// be paused/restarted without affecting other collections; the workflow types
// here mirror the AzureSagaWorkflow shape but route on their own stream so a
// failure in the resilience suite does not contaminate the standard saga
// proof against the assembly-shared Azurite.

[EdictStream("ResilienceEvents")]
public sealed partial record ResilienceTestEvent(Guid AggregateId, int Sequence) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;

    public int Sequence { get; init; } = Sequence;
}

[EdictStream("ResilienceSaga")]
public sealed partial record ResilienceSagaTriggerEvent(Guid WorkflowId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

public sealed partial record ResilienceSagaTrackerCommand(Guid WorkflowId) : EdictCommand
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

[GenerateSerializer]
[Alias("Edict.Azure.Tests.Resilience.ResilienceWorkflowProgress")]
public sealed class ResilienceWorkflowProgress : IEdictPersistedState
{
    [Id(0)]
    public int Handled { get; set; }
}

[GenerateSerializer]
[Alias("Edict.Azure.Tests.Resilience.ResilienceTrackerState")]
public sealed class ResilienceTrackerState : IEdictPersistedState
{
    [Id(0)]
    public int Received { get; set; }

    [Id(1)]
    public Guid LastWorkflowId { get; set; }
}

// Hand-written probes — Orleans codegen needs to see these (ADR 0006).
public interface IResilienceSagaProgressProbe : IGrainWithGuidKey
{
    Task<int> GetHandledAsync();
}

public interface IResilienceSagaTrackerProbe : IGrainWithGuidKey
{
    Task<int> GetReceivedAsync();
    Task<Guid> GetLastWorkflowIdAsync();
}

public partial class ResilienceWorkflowSaga : EdictSaga<ResilienceWorkflowProgress>, IResilienceSagaProgressProbe
{
    public Task Handle(ResilienceSagaTriggerEvent evt)
    {
        Progress.Handled++;
        Dispatch(new ResilienceSagaTrackerCommand(evt.WorkflowId));
        return Task.CompletedTask;
    }

    public Task<int> GetHandledAsync() => Task.FromResult(Progress.Handled);
}

public partial class ResilienceSagaTrackerCommandHandler : EdictCommandHandler<ResilienceTrackerState>, IResilienceSagaTrackerProbe
{
    public Task<EdictCommandResult> Handle(ResilienceSagaTrackerCommand command)
    {
        State.Received++;
        State.LastWorkflowId = command.WorkflowId;
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<int> GetReceivedAsync() => Task.FromResult(State.Received);

    public Task<Guid> GetLastWorkflowIdAsync() => Task.FromResult(State.LastWorkflowId);
}

public interface IResilienceEventPublisher : IGrainWithGuidKey
{
    Task PublishEventAsync(EdictEvent evt);
    Task PublishSagaTriggerAsync(EdictEvent evt);
}

public sealed class ResilienceEventPublisher : Grain, IResilienceEventPublisher
{
    public Task PublishEventAsync(EdictEvent evt)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("ResilienceEvents", this.GetPrimaryKey()));
        return stream.OnNextAsync(evt);
    }

    public Task PublishSagaTriggerAsync(EdictEvent evt)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("ResilienceSaga", this.GetPrimaryKey()));
        return stream.OnNextAsync(evt);
    }
}

public interface IResilienceTestConsumer : IGrainWithGuidKey
{
    Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync();
}

[ImplicitStreamSubscription("ResilienceEvents")]
public sealed class ResilienceTestConsumer : EdictIdempotencyBase, IResilienceTestConsumer
{
    readonly List<Guid> _handledEventIds = [];

    protected override int WindowSize => 16;

    protected override Task<bool> DispatchAsync(EdictEvent evt)
    {
        if (evt is not ResilienceTestEvent rEvt)
        {
            return Task.FromResult(false);
        }
        _handledEventIds.Add(rEvt.EventId);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync() =>
        Task.FromResult<IReadOnlyList<Guid>>(_handledEventIds.AsReadOnly());
}

static class ResilienceWaiters
{
    public static async Task<IReadOnlyList<Guid>> WaitForHandledAsync(
        IResilienceTestConsumer grain,
        int expectedCount = 1,
        int timeoutSeconds = 60)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var ids = await grain.GetHandledEventIdsAsync();
            if (ids.Count >= expectedCount)
            {
                return ids;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
        return await grain.GetHandledEventIdsAsync();
    }

    public static async Task WaitForReceivedAsync(
        IResilienceSagaTrackerProbe tracker,
        int expectedCount = 1,
        int timeoutSeconds = 60)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await tracker.GetReceivedAsync() >= expectedCount)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }
}
