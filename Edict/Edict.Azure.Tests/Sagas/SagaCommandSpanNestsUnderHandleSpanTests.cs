using System.Diagnostics;

using Edict.Telemetry;

namespace Edict.Azure.Tests.Sagas;

/// <summary>
/// trace-context stitch end-to-end on the real Azure Queue + Azure
/// Blob substrate for the saga Event→Command hop: the dispatched
/// command's span tree nests under the saga's handle span as parent-child
/// across the in-grain dispatch (no Azure Queue hop on the command leg — the
/// SendCommand outbox effect resolves via <c>IEdictSender</c>). This guards
/// the orphaned-command-span trap surfaced in #51: capture the traceparent
/// while the handle span is still <c>Activity.Current</c> inside
/// <c>EdictSaga.DispatchEventAsync</c>, never later in
/// <c>CollectPendingOutboxEntries</c>, otherwise the command span lands with
/// no parent.
/// </summary>
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

        // Allow the InvokeHandler / send executors to close their spans —
        // ActivityStopped fires after each using-scope unwinds, which can lag
        // the visible tracker increment by a tick.
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
