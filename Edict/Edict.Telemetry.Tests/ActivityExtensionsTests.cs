using System.Diagnostics;

using Edict.Telemetry;

using Orleans.Runtime;

namespace Edict.Telemetry.Tests;

/// <summary>
/// Unit tests for the Edict.Telemetry surface: identity, span-start extensions,
/// tag-writing extensions, and the RequestContext round-trip. No cluster needed.
/// </summary>
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

    // Cycle 1 — tracer bullet: SourceName identity
    [Fact]
    public void EdictDiagnostics_ShouldHaveSourceNameEdict()
    {
        Assert.Equal("Edict", EdictDiagnostics.SourceName);
    }

    // Cycle 2 — StartEdictCommand starts an activity with the given name
    [Fact]
    public void StartEdictCommand_ShouldStartActivityWithGivenOperationName()
    {
        using (EdictDiagnostics.ActivitySource.StartEdictCommand("edict.command TestCommand"))
        { }

        Assert.Contains(_stopped, a => a.OperationName == "edict.command TestCommand");
    }

    // Cycle 3 — SetEdictCommandTags writes edict.command.route_key
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

    // Cycle 4 — CaptureToRequestContext + ReadRequestContext round-trip
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

    // Cycle 4b — RestoreFromStrings reconstructs ActivityContext from hex strings
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
}
