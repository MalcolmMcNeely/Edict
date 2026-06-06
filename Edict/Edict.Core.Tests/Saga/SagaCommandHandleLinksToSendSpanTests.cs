using System.Diagnostics;

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

        // ActivityStopped fires after each executor's using-scope unwinds, which
        // can lag the visible tracker increment by a tick.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Activity sendSpan;
        Activity handleSpan;
        lock (stopped)
        {
            sendSpan = stopped.Single(a => a.OperationName == $"{SemanticConventions.Commands.Spans.Send} SpanTrackerCommand");
            handleSpan = stopped.Single(a => a.OperationName == $"{SemanticConventions.Commands.Spans.Handle} SpanTrackerCommand");
        }

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
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Activity handleSpan;
        Activity publishSpan;
        lock (stopped)
        {
            handleSpan = stopped.Single(a => a.OperationName == $"{SemanticConventions.Commands.Spans.Handle} SpanTrackerCommand");
            publishSpan = stopped.Single(a => a.OperationName == $"{SemanticConventions.Events.Spans.Publish} SpanTrackerRaisedEvent");
        }

        // An event the dispatched command raises belongs to its turn, not the saga's:
        // it nests under edict.command.handle as parent-child within the new trace.
        Assert.Equal(handleSpan.TraceId, publishSpan.TraceId);
        Assert.Equal(handleSpan.SpanId, publishSpan.ParentSpanId);
    }
}
