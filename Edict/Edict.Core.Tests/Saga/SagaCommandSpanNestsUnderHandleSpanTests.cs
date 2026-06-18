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
        using var capture = new SpanCapture();

        var publisher = _fixture.GrainFactory.GetGrain<ISpanSagaEventPublisher>(workflowId);
        await publisher.PublishAsync(new SpanSagaTriggerEvent(workflowId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        // The edict.command span carries the route-key tag, so it anchors this
        // workflow's trace unambiguously; the trigger handle and the send share that
        // one trace the dispatch hop threads, so scope them to it rather than match
        // by name alone (a parallel collection drives the same workload types).
        var commandSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Commands.Spans.Command} SpanTrackerCommand"
                && SpanWorkflowScoping.BelongsToWorkflow(activity, workflowId),
            "command span for SpanTrackerCommand");
        var handleSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Handle} SpanSagaTriggerEvent"
                && activity.TraceId == commandSpan.TraceId,
            "event handle span for SpanSagaTriggerEvent");
        var sendSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Commands.Spans.Send} SpanTrackerCommand"
                && activity.TraceId == commandSpan.TraceId,
            "send span for SpanTrackerCommand");

        Assert.Equal(handleSpan.SpanId, sendSpan.ParentSpanId);
        Assert.Equal(handleSpan.TraceId, sendSpan.TraceId);
        Assert.Equal(handleSpan.TraceId, commandSpan.TraceId);
    }
}
