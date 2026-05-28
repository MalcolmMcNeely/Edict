using System.Diagnostics;

namespace Edict.Telemetry;

/// <summary>
/// The single <see cref="ActivitySource"/> identity for all Edict instrumentation.
/// Registered by the generated <c>AddEdict()</c>. Keys for the stream-hop
/// <see cref="Orleans.Runtime.RequestContext"/> capture are internal to this assembly.
/// </summary>
public static class EdictDiagnostics
{
    /// <summary>The one OpenTelemetry source name for the framework.</summary>
    public const string SourceName = "Edict";

    /// <summary>The shared activity source used across the command/event spine.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName);

    internal const string TraceIdKey = SemanticConventions.RequestContext.TraceId;
    internal const string SpanIdKey = SemanticConventions.RequestContext.SpanId;
    internal const string TraceStateKey = SemanticConventions.RequestContext.TraceState;
}
