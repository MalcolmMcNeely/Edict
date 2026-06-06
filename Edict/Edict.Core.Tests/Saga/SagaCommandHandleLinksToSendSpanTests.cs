using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using Xunit;

namespace Edict.Core.Tests.Saga;

// The per-turn invariant for the saga's fire-and-forget dispatch: a command a
// saga sends through the outbox is a new grain turn, so its edict.command.handle
// is its own trace root that links back to the edict.command.send producer rather
// than nesting under it. Contrast the awaited API path (CommandSpanTests), where
// edict.command.handle stays a child of the caller's edict.command.
[Collection(SubstrateIndependentCollection.Name)]
public sealed class SagaCommandHandleLinksToSendSpanTests
{
    readonly SubstrateIndependentClusterFixture _fixture;

    public SagaCommandHandleLinksToSendSpanTests(SubstrateIndependentClusterFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CommandHandleSpan_ShouldBeNewRootLinkingToSendSpan_OnSagaDispatchPath()
    {
        var workflowId = Guid.NewGuid();
        using var capture = new SpanCapture();

        var publisher = _fixture.GrainFactory.GetGrain<ISpanSagaEventPublisher>(workflowId);
        await publisher.PublishAsync(new SpanSagaTriggerEvent(workflowId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        var sendSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Commands.Spans.Send} SpanTrackerCommand",
            "send span for SpanTrackerCommand");
        var handleSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Commands.Spans.Handle} SpanTrackerCommand",
            "command handle span for SpanTrackerCommand");

        // The dispatched command's turn is its own trace — it does not share the
        // saga's producing trace.
        Assert.Null(handleSpan.Parent);
        Assert.NotEqual(sendSpan.TraceId, handleSpan.TraceId);

        // ...but it links back to the edict.command.send producer span.
        var onlyLink = Assert.Single(handleSpan.Links);
        Assert.Equal(sendSpan.TraceId, onlyLink.Context.TraceId);
        Assert.Equal(sendSpan.SpanId, onlyLink.Context.SpanId);
    }

    [Fact]
    public async Task RaisedEventPublish_ShouldNestUnderHandle_InTheDispatchedCommandsOwnTrace()
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
            activity => activity.OperationName == $"{SemanticConventions.Commands.Spans.Handle} SpanTrackerCommand",
            "command handle span for SpanTrackerCommand");
        var publishSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Publish} SpanTrackerRaisedEvent",
            "publish span for SpanTrackerRaisedEvent");

        // An event the dispatched command raises belongs to its turn, not the saga's:
        // it nests under edict.command.handle as parent-child within the new trace.
        Assert.Equal(handleSpan.TraceId, publishSpan.TraceId);
        Assert.Equal(handleSpan.SpanId, publishSpan.ParentSpanId);
    }
}
