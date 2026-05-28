using System.Diagnostics;

using Edict.Telemetry;

using Orleans.Runtime;

namespace Edict.Telemetry.Tests;

public sealed class ActivityExtensionsTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _stopped = [];

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
    }

    [Fact]
    public void StartEdictCommand_ShouldStartActivityWithGivenOperationName()
    {
        using (EdictDiagnostics.ActivitySource.StartEdictCommand("edict.command TestCommand"))
        { }

        Assert.Contains(_stopped, a => a.OperationName == "edict.command TestCommand");
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
        Assert.Equal(routeKey, span.GetTagItem("edict.command.route_key"));
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
