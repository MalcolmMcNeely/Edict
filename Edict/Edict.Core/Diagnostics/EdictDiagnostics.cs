using System.Diagnostics;

namespace Edict.Core.Diagnostics;

/// <summary>
/// The single <see cref="ActivitySource"/> for all Edict instrumentation
/// (ADR 0003). Registered by <c>AddEdict()</c>. Spans are wired in a later
/// slice; this slice only owns the source's identity.
/// </summary>
public static class EdictDiagnostics
{
    /// <summary>The one OpenTelemetry source name for the framework.</summary>
    public const string SourceName = "Edict";

    /// <summary>The shared activity source used across the command/event spine.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName);

    // Keys used to carry command-span context through Orleans RequestContext so that
    // FlushRaisedEventsAsync can create publish spans as direct children (ADR 0003).
    internal const string TraceIdKey = "edict.cmd-trace-id";
    internal const string SpanIdKey = "edict.cmd-span-id";
    internal const string TraceStateKey = "edict.cmd-trace-state";
}
