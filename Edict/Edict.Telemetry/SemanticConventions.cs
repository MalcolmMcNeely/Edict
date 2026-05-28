namespace Edict.Telemetry;

/// <summary>
/// Single source of truth for every span name, tag key, ActivitySource name
/// and <see cref="Orleans.Runtime.RequestContext"/> key Edict emits.
/// Span-name constants are exposed as prefix-only (e.g. <see cref="Events.Spans.Publish"/>);
/// call sites compose <c>$"{prefix} {typeName}"</c> so substrate / handler-side
/// <c>StartsWith</c> assertions keep working.
/// </summary>
public static class SemanticConventions
{
    public static class ActivitySources
    {
        /// <summary>The OpenTelemetry source name for the framework — the value passed to <c>TracerProviderBuilder.AddSource</c>.</summary>
        public const string Edict = EdictDiagnostics.SourceName;
    }

    public static class Commands
    {
        public static class Spans
        {
            public const string Command = "edict.command";
            public const string Send = "edict.command.send";
        }

        public static class Tags
        {
            public const string RouteKey = "edict.command.route_key";
        }
    }

    public static class Events
    {
        public static class Spans
        {
            public const string Publish = "edict.event.publish";
            public const string Handle = "edict.event.handle";
            public const string Deduplicated = "edict.event.deduplicated";
        }

        public static class Tags
        {
            public const string Type = "edict.event.type";
            public const string SizeBytes = "edict.event.size_bytes";
            public const string ClaimChecked = "edict.event.claim_checked";
            public const string Deduplicated = "edict.deduplicated";
        }
    }

    public static class ClaimCheck
    {
        public static class Spans
        {
            public const string Get = "edict.event.claim_check.get";
            public const string Put = "edict.event.claim_check.put";
        }

        public static class Tags
        {
            public const string Key = "edict.claim_check.key";
        }
    }

    public static class Tables
    {
        public static class Spans
        {
            public const string Upsert = "edict.table.upsert";
        }
    }

    public static class Telemeterized
    {
        /// <summary>The <c>edict.</c> prefix applied to every Telemeterized tag key. Mirrored generator-side by <c>EdictWellKnownNames.TelemeterizedTagPrefix</c>.</summary>
        public const string Prefix = "edict.";
    }

    public static class Validation
    {
        /// <summary>FluentValidation <c>RootContextData</c> key under which Edict stamps the live grain state for cross-validator access.</summary>
        public const string GrainStateKey = "edict.grain.state";
    }

    internal static class RequestContext
    {
        public const string TraceId = "edict.cmd-trace-id";
        public const string SpanId = "edict.cmd-span-id";
        public const string TraceState = "edict.cmd-trace-state";
    }
}
