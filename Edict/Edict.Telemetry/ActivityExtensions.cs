using System.Diagnostics;

using Orleans.Runtime;

namespace Edict.Telemetry;

/// <summary>
/// Extension methods on <see cref="Activity"/> for Edict tag-writing and the
/// stream-hop <see cref="RequestContext"/> capture / restore.
/// </summary>
public static class ActivityExtensions
{
    /// <summary>Writes <c>edict.command.route_key</c> on the activity.</summary>
    public static void SetEdictCommandTags(this Activity activity, Guid routeKey)
        => activity.SetTag("edict.command.route_key", routeKey);

    /// <summary>
    /// Captures the current activity's W3C trace context into Orleans
    /// <see cref="RequestContext"/> so that <see cref="RestoreFromRequestContext"/>
    /// can reconstitute it on the handler-grain side of the stream hop.
    /// </summary>
    public static void CaptureToRequestContext(this Activity activity)
    {
        RequestContext.Set(EdictDiagnostics.TraceIdKey, activity.TraceId.ToHexString());
        RequestContext.Set(EdictDiagnostics.SpanIdKey, activity.SpanId.ToHexString());
        if (activity.TraceStateString is { } traceState)
            RequestContext.Set(EdictDiagnostics.TraceStateKey, traceState);
    }

    /// <summary>
    /// Reads the raw trace strings stored by <see cref="CaptureToRequestContext"/>.
    /// Callers that need both the <see cref="ActivityContext"/> and the original
    /// strings (e.g. for event stamping fallback) use this overload.
    /// </summary>
    public static (string? TraceId, string? SpanId, string? TraceState) ReadRequestContext()
        => (
            RequestContext.Get(EdictDiagnostics.TraceIdKey) as string,
            RequestContext.Get(EdictDiagnostics.SpanIdKey) as string,
            RequestContext.Get(EdictDiagnostics.TraceStateKey) as string
        );

    /// <summary>
    /// Reconstitutes an <see cref="ActivityContext"/> from raw W3C hex strings.
    /// Returns <see langword="default"/> when the strings are absent or malformed.
    /// </summary>
    public static ActivityContext RestoreFromStrings(
        string? traceId, string? spanId, string? traceState)
    {
        if (traceId is { Length: 32 } && spanId is { Length: 16 })
        {
            return new ActivityContext(
                ActivityTraceId.CreateFromString(traceId),
                ActivitySpanId.CreateFromString(spanId),
                ActivityTraceFlags.Recorded,
                traceState);
        }
        return default;
    }

    /// <summary>
    /// Convenience: reads from <see cref="RequestContext"/> and converts to
    /// <see cref="ActivityContext"/> in one call.
    /// </summary>
    public static ActivityContext RestoreFromRequestContext()
    {
        var (traceId, spanId, traceState) = ReadRequestContext();
        return RestoreFromStrings(traceId, spanId, traceState);
    }

    /// <summary>
    /// Builds a W3C <c>traceparent</c> (sampled) from raw hex trace/span ids,
    /// captured onto an <see cref="Orleans.Runtime"/>-free Outbox entry so a
    /// crash-recovery drain still nests under the originating span.
    /// </summary>
    public static string BuildTraceParent(string traceId, string spanId)
        => $"00-{traceId}-{spanId}-01";

    /// <summary>
    /// Reconstitutes an <see cref="ActivityContext"/> from a W3C
    /// <c>traceparent</c> produced by <see cref="BuildTraceParent"/>. Returns
    /// <see langword="default"/> when absent or malformed.
    /// </summary>
    public static ActivityContext RestoreFromTraceParent(string? traceParent, string? traceState)
    {
        if (traceParent is null)
        {
            return default;
        }

        var parts = traceParent.Split('-');
        return parts.Length == 4
            ? RestoreFromStrings(parts[1], parts[2], traceState)
            : default;
    }
}
