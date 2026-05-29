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

    /// <summary>Cross-cutting attribute keys reused across multiple metric categories
    /// (ADR-0038). Any constant added here must appear on instruments in at least two
    /// categories; per-category attributes stay scoped to their owning category.</summary>
    public static class Common
    {
        public static class Tags
        {
            /// <summary>The CLR type name of the originating grain (e.g. <c>OrderCommandHandler</c>).</summary>
            public const string GrainType = "edict.grain.type";
        }
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
            /// <summary>The CLR name of the concrete <c>EdictCommand</c> subclass being handled.</summary>
            public const string Type = "edict.command.type";
        }

        public static class Meters
        {
            /// <summary>Histogram of command-handle duration in seconds.</summary>
            public const string HandleDuration = "edict.command.handle.duration";
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

        public static class Meters
        {
            /// <summary>Histogram of event-handle duration in seconds.</summary>
            public const string HandleDuration = "edict.event.handle.duration";
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

        public static class Meters
        {
            /// <summary>Histogram of inner-event serialised byte length, sampled on every applied event
            /// (under-threshold and spilled). Records the size distribution operators calibrate
            /// <c>EdictClaimCheckOptions.SoftCapBytes</c> against.</summary>
            public const string PayloadSize = "edict.claim_check.payload.size";
        }
    }

    public static class Outbox
    {
        public static class Tags
        {
            /// <summary>The <see cref="Outbox.Tags.EffectKind"/> ordinal as a string
            /// (<c>PublishEvent</c>, <c>SendCommand</c>, <c>UpsertRow</c>, <c>InvokeHandler</c>).</summary>
            public const string EffectKind = "edict.outbox.effect_kind";
        }

        public static class Meters
        {
            /// <summary>Counter of drain passes (one increment per inner pass of <c>DrainAsync</c>
            /// that found ready entries).</summary>
            public const string DrainCount = "edict.outbox.drain.count";

            /// <summary>Histogram of entries processed per drain pass.</summary>
            public const string DrainEntries = "edict.outbox.drain.entries";
        }
    }

    public static class DeadLetter
    {
        public static class Tags
        {
            /// <summary>Closed allowlist (ADR-0039) classifying the exception that drove a promotion:
            /// <see cref="FailureReasonValues.Timeout"/>, <see cref="FailureReasonValues.Saturated"/>,
            /// <see cref="FailureReasonValues.Serialization"/>, <see cref="FailureReasonValues.Substrate"/>,
            /// or <see cref="FailureReasonValues.Unhandled"/>.</summary>
            public const string FailureReason = "edict.dead_letter.failure_reason";

            /// <summary>The five allowlist values for <see cref="FailureReason"/>. Anything the classifier
            /// can't bucket lands in <see cref="Unhandled"/> rather than leaking the exception type name.</summary>
            public static class FailureReasonValues
            {
                public const string Timeout = "Timeout";
                public const string Saturated = "Saturated";
                public const string Serialization = "Serialization";
                public const string Substrate = "Substrate";
                public const string Unhandled = "Unhandled";
            }
        }

        public static class Meters
        {
            /// <summary>Counter of dead-letter promotions, partitioned by
            /// <see cref="Outbox.Tags.EffectKind"/> and <see cref="Tags.FailureReason"/>.</summary>
            public const string PromotionCount = "edict.dead_letter.promotion.count";
        }
    }

    public static class Idempotency
    {
        public static class Tags
        {
        }

        public static class Meters
        {
            /// <summary>Counter of dedup-window hits (duplicates suppressed before <c>Handle</c>).</summary>
            public const string DuplicateCount = "edict.idempotency.duplicate.count";
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
