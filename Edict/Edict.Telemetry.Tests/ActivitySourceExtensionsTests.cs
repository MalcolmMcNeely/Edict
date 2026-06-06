using System.Diagnostics;

namespace Edict.Telemetry.Tests;

[Collection(ActivityListenerUnitCollection.Name)]
public sealed class ActivitySourceExtensionsTests : IDisposable
{
    readonly ActivityListener _listener;
    readonly List<Activity> _stopped = [];

    public ActivitySourceExtensionsTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = _stopped.Add,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void StartEdictEventHandle_ShouldBeNewRootLinkingToPublish_IgnoringAmbientActivity()
    {
        var publishTraceId = ActivityTraceId.CreateRandom();
        var publishSpanId = ActivitySpanId.CreateRandom();
        var link = new ActivityLink(
            new ActivityContext(publishTraceId, publishSpanId, ActivityTraceFlags.Recorded));

        // An ambient turn that the handle span must NOT inherit — the per-turn
        // invariant bounds each consumer turn to its own trace.
        using var ambient = EdictDiagnostics.ActivitySource.StartEdictCommand("ambient turn");
        Assert.NotNull(ambient);

        using (EdictDiagnostics.ActivitySource.StartEdictEventHandle("OrderPlacedEvent", link))
        {
        }

        var handle = Assert.Single(
            _stopped, a => a.OperationName == $"{SemanticConventions.Events.Spans.Handle} OrderPlacedEvent");
        Assert.Null(handle.Parent);
        Assert.Equal(default, handle.ParentSpanId);
        Assert.NotEqual(ambient!.TraceId, handle.TraceId);

        var onlyLink = Assert.Single(handle.Links);
        Assert.Equal(publishTraceId, onlyLink.Context.TraceId);
        Assert.Equal(publishSpanId, onlyLink.Context.SpanId);
    }

    [Fact]
    public void StartEdictEventDeduplicated_ShouldBeNewRootLinkingToPublish_IgnoringAmbientActivity()
    {
        var publishTraceId = ActivityTraceId.CreateRandom();
        var publishSpanId = ActivitySpanId.CreateRandom();
        var link = new ActivityLink(
            new ActivityContext(publishTraceId, publishSpanId, ActivityTraceFlags.Recorded));

        using var ambient = EdictDiagnostics.ActivitySource.StartEdictCommand("ambient turn");
        Assert.NotNull(ambient);

        using (EdictDiagnostics.ActivitySource.StartEdictEventDeduplicated("OrderPlacedEvent", link))
        {
        }

        var dedup = Assert.Single(
            _stopped, a => a.OperationName == $"{SemanticConventions.Events.Spans.Deduplicated} OrderPlacedEvent");
        Assert.Null(dedup.Parent);
        Assert.NotEqual(ambient!.TraceId, dedup.TraceId);

        var onlyLink = Assert.Single(dedup.Links);
        Assert.Equal(publishTraceId, onlyLink.Context.TraceId);
        Assert.Equal(publishSpanId, onlyLink.Context.SpanId);
    }

    [Fact]
    public void StartEdictDeadLetterPromote_ShouldBeNewRootLinkingToOriginatingContext_IgnoringAmbientActivity()
    {
        var originatingTraceId = ActivityTraceId.CreateRandom();
        var originatingSpanId = ActivitySpanId.CreateRandom();
        var link = new ActivityLink(
            new ActivityContext(originatingTraceId, originatingSpanId, ActivityTraceFlags.Recorded));

        // Promotion runs in a decoupled drain turn, not the originating command's,
        // so the promote span must NOT inherit whatever turn happens to be ambient.
        using var ambient = EdictDiagnostics.ActivitySource.StartEdictCommand("ambient turn");
        Assert.NotNull(ambient);

        using (EdictDiagnostics.ActivitySource.StartEdictDeadLetterPromote("OrderCommandHandler", link))
        {
        }

        var promote = Assert.Single(
            _stopped, a => a.OperationName == $"{SemanticConventions.DeadLetter.Spans.Promote} OrderCommandHandler");
        Assert.Null(promote.Parent);
        Assert.Equal(default, promote.ParentSpanId);
        Assert.NotEqual(ambient!.TraceId, promote.TraceId);

        var onlyLink = Assert.Single(promote.Links);
        Assert.Equal(originatingTraceId, onlyLink.Context.TraceId);
        Assert.Equal(originatingSpanId, onlyLink.Context.SpanId);
    }

    [Fact]
    public void StartEdictDeadLetterPromote_ShouldBeNewRootWithNoLink_WhenLinkAbsent()
    {
        using var ambient = EdictDiagnostics.ActivitySource.StartEdictCommand("ambient turn");

        using (EdictDiagnostics.ActivitySource.StartEdictDeadLetterPromote("OrderCommandHandler", link: null))
        {
        }

        var promote = Assert.Single(
            _stopped, a => a.OperationName == $"{SemanticConventions.DeadLetter.Spans.Promote} OrderCommandHandler");
        Assert.Null(promote.Parent);
        Assert.Empty(promote.Links);
    }

    [Fact]
    public void StartEdictEventHandle_ShouldBeNewRootWithNoLink_WhenLinkAbsent()
    {
        using var ambient = EdictDiagnostics.ActivitySource.StartEdictCommand("ambient turn");

        using (EdictDiagnostics.ActivitySource.StartEdictEventHandle("OrderPlacedEvent", link: null))
        {
        }

        var handle = Assert.Single(
            _stopped, a => a.OperationName == $"{SemanticConventions.Events.Spans.Handle} OrderPlacedEvent");
        Assert.Null(handle.Parent);
        Assert.Empty(handle.Links);
    }
}
