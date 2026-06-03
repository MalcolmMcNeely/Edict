using System.Diagnostics;

using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using Xunit;

namespace Edict.Core.Tests.Saga;

// Guards the orphaned-command-span trap: the traceparent must be captured while
// the handle span is still Activity.Current inside EdictSaga.DispatchEventAsync,
// not later in CollectPendingOutboxEntries. Substrate-agnostic — proven once
// in-memory.
[Collection(SubstrateIndependentCollection.Name)]
public sealed class SagaCommandSpanNestsUnderHandleSpanTests
{
    readonly SubstrateIndependentClusterFixture _fixture;

    public SagaCommandSpanNestsUnderHandleSpanTests(SubstrateIndependentClusterFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CommandSpan_ShouldNestUnderSagaHandleSpan_AcrossTheDispatchHop()
    {
        var workflowId = Guid.NewGuid();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => { lock (stopped) { stopped.Add(activity); } },
        };
        ActivitySource.AddActivityListener(listener);

        var publisher = _fixture.GrainFactory.GetGrain<ISpanSagaEventPublisher>(workflowId);
        await publisher.PublishAsync(new SpanSagaTriggerEvent(workflowId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        var tracker = _fixture.GrainFactory.GetGrain<ISpanTrackerProbe>(workflowId);
        await SpanSagaWaiters.WaitForReceivedAsync(tracker);

        // ActivityStopped fires after each executor's using-scope unwinds,
        // which can lag the visible tracker increment by a tick.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Activity handleSpan;
        Activity sendSpan;
        Activity commandSpan;
        lock (stopped)
        {
            handleSpan = stopped.Single(a => a.OperationName == $"{SemanticConventions.Events.Spans.Handle} SpanSagaTriggerEvent");
            sendSpan = stopped.Single(a => a.OperationName == $"{SemanticConventions.Commands.Spans.Send} SpanTrackerCommand");
            commandSpan = stopped.Single(a => a.OperationName == $"{SemanticConventions.Commands.Spans.Command} SpanTrackerCommand");
        }

        Assert.Equal(handleSpan.SpanId, sendSpan.ParentSpanId);
        Assert.Equal(handleSpan.TraceId, sendSpan.TraceId);
        Assert.Equal(handleSpan.TraceId, commandSpan.TraceId);
    }
}
