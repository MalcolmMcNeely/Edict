using System.Diagnostics;

using Edict.Core.Tests.Grains;
using Edict.Telemetry;

namespace Edict.Core.Tests.Saga;

[Collection(EdictClusterCollection.Name)]
public sealed class SagaWorkflowTests(EdictClusterFixture fixture)
{
    // Cycle 2 — tracer bullet: an event drives the saga, which dispatches
    // exactly one Command via the SendCommand outbox effect, and its durable
    // Progress commits in the same write (ADR 0020).
    [Fact]
    public async Task SagaHandle_ShouldDispatchOneCommandAndPersistProgress_WhenEventDelivered()
    {
        var workflowId = Guid.NewGuid();

        var publisher = fixture.Cluster.GrainFactory.GetGrain<IProjectionPublisherGrain>(workflowId);
        await publisher.PublishToStreamAsync("SagaWorkflow", new SagaTriggerEvent(workflowId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        var tracker = fixture.Cluster.GrainFactory.GetGrain<ISagaTrackerProbe>(workflowId);
        await WaitForAsync(async () => await tracker.GetReceivedAsync() >= 1);

        var saga = fixture.Cluster.GrainFactory.GetGrain<ISagaProgressProbe>(workflowId);

        Assert.Equal(1, await tracker.GetReceivedAsync());
        Assert.Equal(workflowId, await tracker.GetLastWorkflowIdAsync());
        Assert.Equal(1, await saga.GetHandledAsync());
    }

    // Cycle 3 — the saga's Event→Command hop stays parent-child (ADR 0003):
    // the dispatched command's span tree nests under the saga handle span.
    [Fact]
    public async Task CommandSpan_ShouldNestUnderSagaHandleSpan_AcrossTheDispatchHop()
    {
        var workflowId = Guid.NewGuid();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var publisher = fixture.Cluster.GrainFactory.GetGrain<IProjectionPublisherGrain>(workflowId);
        await publisher.PublishToStreamAsync("SagaWorkflow", new SagaTriggerEvent(workflowId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        var tracker = fixture.Cluster.GrainFactory.GetGrain<ISagaTrackerProbe>(workflowId);
        await WaitForAsync(async () => await tracker.GetReceivedAsync() >= 1);

        var handleSpan = stopped.Single(a => a.OperationName == "edict.event.handle SagaTriggerEvent");
        var sendSpan = stopped.Single(a => a.OperationName == "edict.command.send SagaTrackerCommand");
        var commandSpan = stopped.Single(a => a.OperationName == "edict.command SagaTrackerCommand");

        Assert.Equal(handleSpan.SpanId, sendSpan.ParentSpanId);
        Assert.Equal(handleSpan.TraceId, sendSpan.TraceId);
        Assert.Equal(handleSpan.TraceId, commandSpan.TraceId);
    }

    static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
