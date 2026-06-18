using System.Diagnostics;

using Edict.Telemetry;

using Xunit;

namespace Edict.Core.Tests.Saga;

// A fired saga cap is its own grain turn, far removed in time from the event that
// armed the saga, so edict.saga.timeout is a new trace root that links back to the
// arm-context captured on the saga's first handled event. The arm-context is
// persisted on the SagaLifecycle slot, so the link survives a deactivation between
// arming and the cap firing.
[Collection(SagaLifecycleCollection.Name)]
public sealed class SagaTimeoutSpanTests
{
    // Deterministic W3C ids stamped on the arming event so the link target is exact.
    const string ArmTraceId = "0af7651916cd43dd8448eb211c80319c";
    const string ArmSpanId = "b7ad6b7169203331";

    readonly SagaLifecycleClusterFixture _fixture;

    public SagaTimeoutSpanTests(SagaLifecycleClusterFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FiredCap_ShouldBeNewRootLinkingToSagaArmContext()
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

        var saga = _fixture.GrainFactory.GetGrain<ISagaLifecycleProbe>(
            workflowId, grainClassNamePrefix: typeof(CappedSaga).FullName);

        // The arming event carries a known trace context; the saga captures it as it
        // arms its cap on the first handle.
        await saga.DeliverAsync(new LifecycleTriggerEvent(workflowId)
        {
            EventId = Guid.NewGuid(),
            TraceId = ArmTraceId,
            SpanId = ArmSpanId,
        });

        SagaLifecycleClusterFixture.Time.Advance(TimeSpan.FromMinutes(2));
        await saga.FireCapAsync();

        var armSpanId = ActivitySpanId.CreateFromString(ArmSpanId);
        Activity timeoutSpan;
        lock (stopped)
        {
            timeoutSpan = stopped.Single(a =>
                a.OperationName == $"{SemanticConventions.Sagas.Spans.Timeout} CappedSaga"
                && a.Links.Any(link => link.Context.SpanId == armSpanId));
        }

        // The fired cap is its own trace — it does not share the arming event's.
        Assert.Null(timeoutSpan.Parent);
        Assert.NotEqual(ActivityTraceId.CreateFromString(ArmTraceId), timeoutSpan.TraceId);

        // ...but it links back to the context that armed the saga.
        var onlyLink = Assert.Single(timeoutSpan.Links);
        Assert.Equal(ActivityTraceId.CreateFromString(ArmTraceId), onlyLink.Context.TraceId);
        Assert.Equal(armSpanId, onlyLink.Context.SpanId);
    }

    [Fact]
    public async Task FiredCap_ShouldCarryAFreshConversationId_DistinctFromTheArmingEvent()
    {
        var workflowId = Guid.NewGuid();
        var armingConversationId = Guid.NewGuid();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => { lock (stopped) { stopped.Add(activity); } },
        };
        ActivitySource.AddActivityListener(listener);

        var saga = _fixture.GrainFactory.GetGrain<ISagaLifecycleProbe>(
            workflowId, grainClassNamePrefix: typeof(CappedSaga).FullName);

        await saga.DeliverAsync(new LifecycleTriggerEvent(workflowId)
        {
            EventId = Guid.NewGuid(),
            TraceId = ArmTraceId,
            SpanId = ArmSpanId,
            ConversationId = armingConversationId,
        });

        SagaLifecycleClusterFixture.Time.Advance(TimeSpan.FromMinutes(2));
        await saga.FireCapAsync();

        var armSpanId = ActivitySpanId.CreateFromString(ArmSpanId);
        Activity timeoutSpan;
        lock (stopped)
        {
            timeoutSpan = stopped.Single(a =>
                a.OperationName == $"{SemanticConventions.Sagas.Spans.Timeout} CappedSaga"
                && a.Links.Any(link => link.Context.SpanId == armSpanId));
        }

        // A fired cap is a fresh causal root: its compensation reads as its own
        // conversation, not the conversation that armed the saga.
        var capConversationId = Assert.IsType<string>(timeoutSpan.GetTagItem(SemanticConventions.Messaging.Tags.ConversationId));
        Assert.NotEqual(Guid.Empty, Guid.Parse(capConversationId));
        Assert.NotEqual(armingConversationId.ToString(), capConversationId);
    }
}
