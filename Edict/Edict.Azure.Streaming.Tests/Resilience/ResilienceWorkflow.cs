using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Commands;
using Edict.Core.Idempotency;
using Edict.Core.Projections;
using Edict.Core.Sagas;
using Edict.Core.TableStorage;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Azure.Streaming.Tests.Resilience;

// Dedicated event/saga types for the Azure streaming-axis transport-fault suite.
// The resilience cluster owns its Azurite container so it can be paused/restarted
// without affecting other collections; these types route on their own streams so
// a failure here does not contaminate the standard streaming proofs against the
// assembly-shared Azurite. Persistence is the dumb reference (memory grain
// storage + the in-memory reference table store) — the broker is the fault point
// and the only axis asserted.
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
[Alias("Edict.Azure.Streaming.Tests.Resilience.ResilienceWorkflowProgress")]
public sealed class ResilienceWorkflowProgress : IEdictPersistedState
{
    [Id(0)]
    public int Handled { get; set; }
}

[GenerateSerializer]
[Alias("Edict.Azure.Streaming.Tests.Resilience.ResilienceTrackerState")]
public sealed class ResilienceTrackerState : IEdictPersistedState
{
    [Id(0)]
    public int Received { get; set; }

    [Id(1)]
    public Guid LastWorkflowId { get; set; }
}

// Hand-written probes — Orleans codegen needs to see these.
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
    Task HandleAsync(ResilienceSagaTriggerEvent edictEvent)
    {
        Progress.Handled++;
        Dispatch(new ResilienceSagaTrackerCommand(edictEvent.WorkflowId));
        return Task.CompletedTask;
    }

    public Task<int> GetHandledAsync() => Task.FromResult(Progress.Handled);
}

public partial class ResilienceSagaTrackerCommandHandler : EdictCommandHandler<ResilienceTrackerState>, IResilienceSagaTrackerProbe
{
    Task<EdictCommandResult> HandleAsync(ResilienceSagaTrackerCommand command)
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
    Task PublishEventAsync(EdictEvent edictEvent);
    Task PublishSagaTriggerAsync(EdictEvent edictEvent);
}

public sealed class ResilienceEventPublisher : Grain, IResilienceEventPublisher
{
    public Task PublishEventAsync(EdictEvent edictEvent)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("ResilienceEvents", this.GetPrimaryKey()));
        return stream.OnNextAsync(edictEvent);
    }

    public Task PublishSagaTriggerAsync(EdictEvent edictEvent)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("ResilienceSaga", this.GetPrimaryKey()));
        return stream.OnNextAsync(edictEvent);
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

    protected override Task<EdictDispatchOutcome> DispatchAsync(EdictEvent edictEvent)
    {
        if (edictEvent is not ResilienceTestEvent resilienceEvent)
        {
            return Task.FromResult(EdictDispatchOutcome.NotHandled);
        }
        _handledEventIds.Add(resilienceEvent.EventId);
        return Task.FromResult(EdictDispatchOutcome.HandledWithNoEffect);
    }

    public Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync() =>
        Task.FromResult<IReadOnlyList<Guid>>(_handledEventIds.AsReadOnly());
}

// A slow projection whose first Handle invocation blocks long enough for the
// test to KillSiloAsync the hosting silo mid-flight. The grain captures the
// hosting silo's address into SiloKillCoordinator so the test can target the
// kill at the silo that actually owns the activation. The kill lands before the
// atomic ring + UpsertRow commit, so the first turn writes nothing; after
// redelivery the projection row is written exactly once into the reference store.
[EdictStream("SiloKillProjection")]
public sealed partial record SiloKillProjectionEvent(Guid AggregateId) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;
}

[GenerateSerializer]
[Alias("Edict.Azure.Streaming.Tests.Resilience.SiloKillTableRow")]
public sealed class SiloKillTableRow : IEdictPersistedState
{
    [Id(0)]
    public int Count { get; set; }
}

public interface ISiloKillEventPublisher : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent edictEvent);
}

public sealed class SiloKillEventPublisher : Grain, ISiloKillEventPublisher
{
    public Task PublishAsync(EdictEvent edictEvent) =>
        this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("SiloKillProjection", this.GetPrimaryKey()))
            .OnNextAsync(edictEvent);
}

public sealed partial class SiloKillProjectionBuilder : EdictTableProjectionBuilder<SiloKillTableRow>
{
    public const string Table = "silokillprojection";

    readonly ILocalSiloDetails _siloDetails;

    public SiloKillProjectionBuilder(
        IEdictTableStoreFactory storeFactory,
        ILocalSiloDetails siloDetails)
        : base(storeFactory)
    {
        _siloDetails = siloDetails;
    }

    protected override string TableName => Table;

    protected override string GetRowKey(EdictEvent edictEvent) =>
        edictEvent switch
        {
            SiloKillProjectionEvent e => e.AggregateId.ToString(),
            _ => this.GetPrimaryKey().ToString(),
        };

    async Task HandleAsync(SiloKillProjectionEvent edictEvent)
    {
        var entry = Interlocked.Increment(ref SiloKillCoordinator.HandlerEntries);
        if (entry == 1)
        {
            // First delivery: announce the hosting silo and block long enough
            // for the test to KillSiloAsync this silo before Handle returns.
            // The kill tears down the activation so the upsert effect is never
            // staged and the dedup ring never commits — the queue message
            // returns to "visible" after the fixture's 5s visibility timeout and
            // a surviving silo picks it up.
            SiloKillCoordinator.HandlerEntered.TrySetResult(_siloDetails.SiloAddress);
            await Task.Delay(TimeSpan.FromSeconds(20));
        }
        CurrentRow.Count++;
    }
}

public static class SiloKillCoordinator
{
    public static TaskCompletionSource<SiloAddress> HandlerEntered { get; private set; }
        = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static int HandlerEntries;

    public static void Reset()
    {
        HandlerEntries = 0;
        HandlerEntered = new TaskCompletionSource<SiloAddress>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public static Task<SiloAddress> WaitForHandlerEnteredAsync(TimeSpan timeout) =>
        HandlerEntered.Task.WaitAsync(timeout);
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
