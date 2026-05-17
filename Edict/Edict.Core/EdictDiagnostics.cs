using System.Diagnostics;

namespace Edict.Core;

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
}
