using System.Diagnostics;

using Edict.Telemetry;

namespace Edict.Azure.Tests.Sagas;

// Guards the orphaned-command-span trap: the traceparent must be captured
// while the handle span is still Activity.Current inside
// EdictSaga.DispatchEventAsync, not later in CollectPendingOutboxEntries.
[Collection(AzureClusterCollection.Name)]
public sealed class SagaCommandSpanNestsUnderHandleSpanTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task CommandSpan_ShouldNestUnderSagaHandleSpan_AcrossTheDispatchHop()
    {
        var workflowId = Guid.NewGuid();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => { lock (stopped) { stopped.Add(a); } },
        };
        ActivitySource.AddActivityListener(listener);

        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureSagaEventPublisher>(workflowId);
        await publisher.PublishAsync(new AzureSagaTriggerEvent(workflowId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        var tracker = fixture.Cluster.GrainFactory.GetGrain<IAzureSagaTrackerProbe>(workflowId);
        await AzureSagaWaiters.WaitForReceivedAsync(tracker);

        // ActivityStopped fires after each executor's using-scope unwinds,
        // which can lag the visible tracker increment by a tick.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Activity handleSpan;
        Activity sendSpan;
        Activity commandSpan;
        lock (stopped)
        {
            handleSpan = stopped.Single(a => a.OperationName == "edict.event.handle AzureSagaTriggerEvent");
            sendSpan = stopped.Single(a => a.OperationName == "edict.command.send AzureSagaTrackerCommand");
            commandSpan = stopped.Single(a => a.OperationName == "edict.command AzureSagaTrackerCommand");
        }

        Assert.Equal(handleSpan.SpanId, sendSpan.ParentSpanId);
        Assert.Equal(handleSpan.TraceId, sendSpan.TraceId);
        Assert.Equal(handleSpan.TraceId, commandSpan.TraceId);
    }
}
