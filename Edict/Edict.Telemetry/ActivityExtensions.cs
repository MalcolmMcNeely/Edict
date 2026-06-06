using System.Diagnostics;
using System.Globalization;

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
        => activity.SetTag(SemanticConventions.Commands.Tags.RouteKey, routeKey);

    /// <summary>
    /// Captures the current activity's W3C trace context into Orleans
    /// <see cref="RequestContext"/> so that <see cref="RestoreFromRequestContext"/>
    /// can reconstitute it on the handler-grain side of the stream hop.
    /// </summary>
    public static void CaptureToRequestContext(this Activity activity)
    {
        RequestContext.Set(EdictDiagnostics.TraceIdKey, activity.TraceId.ToHexString());
        RequestContext.Set(EdictDiagnostics.SpanIdKey, activity.SpanId.ToHexString());
        RequestContext.Set(EdictDiagnostics.RecordedKey, activity.Recorded);
        if (activity.TraceStateString is { } traceState)
        {
            RequestContext.Set(EdictDiagnostics.TraceStateKey, traceState);
        }
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
    /// <paramref name="recorded"/> sets the sampled flag on the restored context so
    /// a dropped head decision stays dropped across the hop and a sampled one stays
    /// recorded — the caller supplies the real flag instead of the context forcing it.
    /// </summary>
    public static ActivityContext RestoreFromStrings(
        string? traceId, string? spanId, string? traceState, bool recorded = true)
    {
        if (traceId is { Length: 32 } && spanId is { Length: 16 })
        {
            return new ActivityContext(
                ActivityTraceId.CreateFromString(traceId),
                ActivitySpanId.CreateFromString(spanId),
                recorded ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None,
                traceState);
        }
        return default;
    }

    /// <summary>
    /// Convenience: reads from <see cref="RequestContext"/> and converts to
    /// <see cref="ActivityContext"/> in one call, honouring the sampled flag the
    /// capturing command span carried so the restored context reflects the real
    /// head-sampling decision.
    /// </summary>
    public static ActivityContext RestoreFromRequestContext()
    {
        var (traceId, spanId, traceState) = ReadRequestContext();
        var recorded = RequestContext.Get(EdictDiagnostics.RecordedKey) is true;
        return RestoreFromStrings(traceId, spanId, traceState, recorded);
    }

    /// <summary>
    /// Builds a W3C <c>traceparent</c> (sampled) from raw hex trace/span ids,
    /// captured onto an <see cref="Orleans.Runtime"/>-free Outbox entry so a
    /// crash-recovery drain still nests under the originating span.
    /// </summary>
    public static string BuildTraceParent(string traceId, string spanId)
        => $"00-{traceId}-{spanId}-01";

    /// <summary>
    /// Returns the W3C <c>traceparent</c> for an <see cref="Activity"/> using
    /// its lazily-cached <see cref="Activity.Id"/>. Zero per-call allocation
    /// after first read, and the flag byte reflects the activity's actual
    /// recording decision instead of the forced <c>-01</c> of the string-pair
    /// overload.
    /// </summary>
    public static string BuildTraceParent(this Activity activity)
        => activity.Id!;

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
        if (parts.Length != 4)
        {
            return default;
        }

        var recorded = byte.TryParse(parts[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var flags)
            && (flags & (byte)ActivityTraceFlags.Recorded) != 0;
        return RestoreFromStrings(parts[1], parts[2], traceState, recorded);
    }

    /// <summary>
    /// Builds an <see cref="ActivityLink"/> from a persisted W3C <c>traceparent</c>
    /// and <c>tracestate</c> — the cross-turn causal edge used where a span links
    /// back to the context that caused it rather than nesting under it. Returns
    /// <see langword="null"/> when the traceparent is absent or malformed; the
    /// tracestate rides onto the link's context.
    /// </summary>
    public static ActivityLink? BuildLink(string? traceParent, string? traceState)
    {
        var context = RestoreFromTraceParent(traceParent, traceState);
        return context.TraceId == default ? null : new ActivityLink(context);
    }
}
