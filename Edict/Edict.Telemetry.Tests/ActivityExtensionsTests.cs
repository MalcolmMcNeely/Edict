using System.Diagnostics;

using Edict.Telemetry;

using Orleans.Runtime;

namespace Edict.Telemetry.Tests;

public sealed class ActivityExtensionsTests : IDisposable
{
    readonly ActivityListener _listener;
    readonly List<Activity> _stopped = [];

    public ActivityExtensionsTests()
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
    public void EdictDiagnostics_ShouldHaveSourceNameEdict()
    {
        Assert.Equal("Edict", EdictDiagnostics.SourceName);
        Assert.Equal(EdictDiagnostics.SourceName, SemanticConventions.ActivitySources.Edict);
    }

    [Fact]
    public void StartEdictCommand_ShouldStartActivityWithGivenOperationName()
    {
        var operationName = $"{SemanticConventions.Commands.Spans.Command} TestCommand";
        using (EdictDiagnostics.ActivitySource.StartEdictCommand(operationName))
        { }

        Assert.Contains(_stopped, a => a.OperationName == operationName);
    }

    [Fact]
    public void SetEdictCommandTags_ShouldSetEdictCommandRouteKeyTag()
    {
        var routeKey = Guid.NewGuid();

        using (var activity = EdictDiagnostics.ActivitySource.StartEdictCommand("test"))
        {
            activity?.SetEdictCommandTags(routeKey);
        }

        var span = Assert.Single(_stopped);
        Assert.Equal(routeKey, span.GetTagItem(SemanticConventions.Commands.Tags.RouteKey));
    }

    [Fact]
    public void ReadRequestContext_ShouldReturnSameTraceIds_WhenPreviouslyCaptured()
    {
        string? capturedTraceId, capturedSpanId;

        using (var activity = EdictDiagnostics.ActivitySource.StartEdictCommand("test"))
        {
            activity!.CaptureToRequestContext();
            (capturedTraceId, capturedSpanId, _) = ActivityExtensions.ReadRequestContext();
        }

        var span = Assert.Single(_stopped);
        Assert.Equal(span.TraceId.ToHexString(), capturedTraceId);
        Assert.Equal(span.SpanId.ToHexString(), capturedSpanId);
    }

    [Fact]
    public void RestoreFromStrings_ShouldReconstructActivityContext_WhenStringsAreValid()
    {
        ActivityContext original;
        using (var activity = EdictDiagnostics.ActivitySource.StartEdictCommand("test"))
        {
            original = activity!.Context;
        }

        var traceId = original.TraceId.ToHexString();
        var spanId = original.SpanId.ToHexString();

        var restored = ActivityExtensions.RestoreFromStrings(traceId, spanId, null);

        Assert.Equal(original.TraceId, restored.TraceId);
        Assert.Equal(original.SpanId, restored.SpanId);
    }

    [Fact]
    public void RestoreFromStrings_ShouldReturnDefault_WhenStringsAreNull()
    {
        var result = ActivityExtensions.RestoreFromStrings(null, null, null);
        Assert.Equal(default, result);
    }

    [Fact]
    public void RestoreFromStrings_ShouldStayRecorded_WhenRecordedTrue()
    {
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var spanId = ActivitySpanId.CreateRandom().ToHexString();

        var restored = ActivityExtensions.RestoreFromStrings(traceId, spanId, null, recorded: true);

        Assert.Equal(ActivityTraceFlags.Recorded, restored.TraceFlags & ActivityTraceFlags.Recorded);
    }

    [Fact]
    public void RestoreFromStrings_ShouldStayDropped_WhenRecordedFalse()
    {
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var spanId = ActivitySpanId.CreateRandom().ToHexString();

        var restored = ActivityExtensions.RestoreFromStrings(traceId, spanId, null, recorded: false);

        Assert.Equal(ActivityTraceFlags.None, restored.TraceFlags & ActivityTraceFlags.Recorded);
    }

    [Fact]
    public void RestoreFromRequestContext_ShouldStayRecorded_WhenCapturedActivityWasSampled()
    {
        using var sampledSource = new ActivitySource("Edict.Test.Sampled");
        using var recordedListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sampledSource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _ => { },
        };
        ActivitySource.AddActivityListener(recordedListener);

        ActivityContext restored;
        using (var activity = sampledSource.StartActivity("sampled"))
        {
            Assert.True(activity!.Recorded);
            activity.CaptureToRequestContext();
            restored = ActivityExtensions.RestoreFromRequestContext();
        }

        Assert.Equal(ActivityTraceFlags.Recorded, restored.TraceFlags & ActivityTraceFlags.Recorded);
    }

    [Fact]
    public void RestoreFromRequestContext_ShouldStayDropped_WhenCapturedActivityWasNotSampled()
    {
        using var unsampledSource = new ActivitySource("Edict.Test.Unsampled");
        using var propagationOnlyListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == unsampledSource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.PropagationData,
            ActivityStopped = _ => { },
        };
        ActivitySource.AddActivityListener(propagationOnlyListener);

        ActivityContext restored;
        using (var activity = unsampledSource.StartActivity("dropped"))
        {
            Assert.False(activity!.Recorded);
            activity.CaptureToRequestContext();
            restored = ActivityExtensions.RestoreFromRequestContext();
        }

        Assert.Equal(ActivityTraceFlags.None, restored.TraceFlags & ActivityTraceFlags.Recorded);
    }

    [Fact]
    public void RestoreFromTraceParent_ShouldStayRecorded_WhenFlagByteIsSampled()
    {
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var spanId = ActivitySpanId.CreateRandom().ToHexString();
        var sampledTraceParent = $"00-{traceId}-{spanId}-01";

        var restored = ActivityExtensions.RestoreFromTraceParent(sampledTraceParent, null);

        Assert.Equal(ActivityTraceFlags.Recorded, restored.TraceFlags & ActivityTraceFlags.Recorded);
    }

    [Fact]
    public void RestoreFromTraceParent_ShouldStayDropped_WhenFlagByteIsUnsampled()
    {
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var spanId = ActivitySpanId.CreateRandom().ToHexString();
        var unsampledTraceParent = $"00-{traceId}-{spanId}-00";

        var restored = ActivityExtensions.RestoreFromTraceParent(unsampledTraceParent, null);

        Assert.Equal(ActivityTraceFlags.None, restored.TraceFlags & ActivityTraceFlags.Recorded);
    }

    [Fact]
    public void BuildLink_ShouldReturnLinkWithMatchingContext_WhenTraceParentValid()
    {
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var spanId = ActivitySpanId.CreateRandom().ToHexString();
        var traceParent = ActivityExtensions.BuildTraceParent(traceId, spanId);

        var link = ActivityExtensions.BuildLink(traceParent, null);

        Assert.NotNull(link);
        Assert.Equal(traceId, link.Value.Context.TraceId.ToHexString());
        Assert.Equal(spanId, link.Value.Context.SpanId.ToHexString());
    }

    [Fact]
    public void BuildLink_ShouldReturnNull_WhenTraceParentNull()
        => Assert.Null(ActivityExtensions.BuildLink(null, null));

    [Fact]
    public void BuildLink_ShouldReturnNull_WhenTraceParentMalformed()
        => Assert.Null(ActivityExtensions.BuildLink("garbage", null));

    [Fact]
    public void BuildLink_ShouldCarryTraceState_OntoLink()
    {
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var spanId = ActivitySpanId.CreateRandom().ToHexString();
        var traceParent = ActivityExtensions.BuildTraceParent(traceId, spanId);

        var link = ActivityExtensions.BuildLink(traceParent, "vendor=abc");

        Assert.NotNull(link);
        Assert.Equal("vendor=abc", link.Value.Context.TraceState);
    }

    [Fact]
    public void BuildTraceParent_Activity_ShouldReturnW3CFormatReflectingRecordingFlag()
    {
        Activity? captured;
        string? traceParent;
        using (var activity = EdictDiagnostics.ActivitySource.StartEdictCommand("test"))
        {
            captured = activity;
            traceParent = activity!.BuildTraceParent();
        }

        var expectedFlags = (captured!.ActivityTraceFlags & ActivityTraceFlags.Recorded) != 0 ? "01" : "00";
        Assert.Equal($"00-{captured.TraceId.ToHexString()}-{captured.SpanId.ToHexString()}-{expectedFlags}", traceParent);
    }

    [Fact]
    public void BuildTraceParent_Activity_ShouldReturnActivityIdReference()
    {
        using var activity = EdictDiagnostics.ActivitySource.StartEdictCommand("test");

        Assert.Same(activity!.Id, activity.BuildTraceParent());
    }
}
