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

        var handleSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Handle} SpanSagaTriggerEvent",
            "event handle span for SpanSagaTriggerEvent");
        var sendSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Commands.Spans.Send} SpanTrackerCommand",
            "send span for SpanTrackerCommand");
        var commandSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Commands.Spans.Command} SpanTrackerCommand",
            "command span for SpanTrackerCommand");

        Assert.Equal(handleSpan.SpanId, sendSpan.ParentSpanId);
        Assert.Equal(handleSpan.TraceId, sendSpan.TraceId);
        Assert.Equal(handleSpan.TraceId, commandSpan.TraceId);
    }
}
