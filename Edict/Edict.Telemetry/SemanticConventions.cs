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

    /// <summary>Cross-cutting attribute keys reused across multiple metric categories.
    /// Any constant added here must appear on instruments in at least two
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
            public const string Handle = "edict.command.handle";
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

            /// <summary>Histogram of producer-to-consumer event lag in seconds —
            /// <c>now − <see cref="Edict.Contracts.Events.EdictEvent.OccurredAt"/></c>
            /// recorded at handle entry. The framework stamps <c>OccurredAt</c> at
            /// <c>Raise</c> time so the value is true intent-to-handle lag, not
            /// wire-time lag.</summary>
            public const string HandleLag = "edict.event.handle.lag";
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

            /// <summary>Observable gauge of pending outbox entries summed per grain type
            /// across every live host on this silo. Sourced from the silo-local
            /// metrics cache so a scrape costs zero grain calls.</summary>
            public const string PendingCount = "edict.outbox.pending.count";

            /// <summary>Observable gauge of the oldest pending outbox entry's age in
            /// seconds, per grain type, taken as <c>max(now − enqueuedAt_i)</c> across
            /// every live host on this silo. Sourced from the silo-local metrics
            /// cache.</summary>
            public const string OldestEntryAge = "edict.outbox.oldest_entry.age";
        }
    }

    public static class Schedules
    {
        public static class Spans
        {
            /// <summary>The schedule-fire turn: a recurring tick or one-shot fire dispatched
            /// off the durable schedule. A new trace root linking back to the arming command.</summary>
            public const string Fire = "edict.schedule.fire";
        }
    }

    public static class Sagas
    {
        public static class Spans
        {
            /// <summary>The saga absolute-lifetime cap fire. A new trace root linking back to
            /// the context that armed the saga on its first handled event.</summary>
            public const string Timeout = "edict.saga.timeout";
        }

        public static class Tags
        {
            /// <summary>How a fired absolute-lifetime cap resolved:
            /// <see cref="OutcomeValues.Compensated"/> when the
            /// <c>OnSagaTimeoutAsync</c> override dispatched a compensating Command,
            /// <see cref="OutcomeValues.Deadlettered"/> otherwise.</summary>
            public const string Outcome = "edict.saga.timeout.outcome";

            /// <summary>The closed allowlist of values for <see cref="Outcome"/>.</summary>
            public static class OutcomeValues
            {
                public const string Compensated = "compensated";
                public const string Deadlettered = "deadlettered";
            }
        }

        public static class Meters
        {
            /// <summary>Observable gauge of seconds since the last event a saga handled,
            /// per saga type, taken as <c>max(now − lastHandledAt_i)</c> across every
            /// live saga on this silo. Sourced from the silo-local metrics cache.</summary>
            public const string ProgressAge = "edict.saga.progress.age";

            /// <summary>Counter of fired absolute-lifetime caps, partitioned by saga type
            /// (<see cref="Common.Tags.GrainType"/>) and <see cref="Tags.Outcome"/>.
            /// With <see cref="Completed"/> it makes the health ratio
            /// <c>fired / (fired + completed)</c> computable, separating a handful of
            /// timeouts among millions from a rising failure trend.</summary>
            public const string TimeoutFired = "edict.saga.timeout.fired";

            /// <summary>Counter of sagas that reached <c>Completed</c> via <c>Complete()</c>,
            /// partitioned by saga type (<see cref="Common.Tags.GrainType"/>). The
            /// denominator companion to <see cref="TimeoutFired"/>.</summary>
            public const string Completed = "edict.saga.completed";
        }
    }

    public static class DeadLetter
    {
        public static class Spans
        {
            /// <summary>The dead-letter promotion turn: a failed effect being promoted to a
            /// forensic dead-letter row. A new trace root linking back to the failing entry's
            /// originating context, since promotion runs in a decoupled drain turn rather than
            /// the originating command's.</summary>
            public const string Promote = "edict.dead_letter.promote";
        }

        public static class Tags
        {
            /// <summary>Closed allowlist classifying the exception that drove a promotion:
            /// <see cref="FailureReasonValues.Timeout"/>, <see cref="FailureReasonValues.Saturated"/>,
            /// <see cref="FailureReasonValues.Serialization"/>, <see cref="FailureReasonValues.Substrate"/>,
            /// <see cref="FailureReasonValues.Wiring"/>, <see cref="FailureReasonValues.ConsumerBug"/>,
            /// <see cref="FailureReasonValues.InternalBug"/>, or <see cref="FailureReasonValues.Unhandled"/>.</summary>
            public const string FailureReason = "edict.dead_letter.failure_reason";

            /// <summary>The allowlist values for <see cref="FailureReason"/>. Anything the classifier
            /// can't bucket lands in <see cref="Unhandled"/> rather than leaking the exception type name.
            /// The set is closed at compile time so the dimension stays bounded. <see cref="SagaTimeout"/>
            /// (an un-overridden cap fired) and <see cref="SagaTerminal"/> (a new Event at a terminal saga)
            /// are distinct from <see cref="ConsumerBug"/> so the two saga-lifecycle failures read apart on
            /// the dead-letter projection.</summary>
            public static class FailureReasonValues
            {
                public const string Timeout = "Timeout";
                public const string Saturated = "Saturated";
                public const string Serialization = "Serialization";
                public const string Substrate = "Substrate";
                public const string Wiring = "Wiring";
                public const string ConsumerBug = "ConsumerBug";
                public const string InternalBug = "InternalBug";
                public const string SagaTimeout = "SagaTimeout";
                public const string SagaTerminal = "SagaTerminal";
                public const string ScheduleTimeout = "ScheduleTimeout";
                public const string Unhandled = "Unhandled";
            }

            /// <summary>Closed allowlist tagging an <see cref="Meters.PromotionFailureCount"/>
            /// increment with the reason the promoter could not build a normal
            /// dead-letter row. Distinct from <see cref="FailureReason"/> because
            /// the values describe internal promoter faults — not the upstream
            /// exception that drove the original effect to dead-letter.</summary>
            public const string PromotionFailureReason = "edict.dead_letter.promotion_failure_reason";

            /// <summary>The allowlist values for <see cref="PromotionFailureReason"/>.
            /// The set is closed at compile time so the dimension stays bounded per ADR 0039.</summary>
            public static class PromotionFailureReasonValues
            {
                public const string UnsupportedKind = "unsupported_kind";
                public const string MissingRouteKey = "missing_route_key";
                public const string SerializationFailure = "serialization_failure";
            }
        }

        public static class Meters
        {
            /// <summary>Counter of dead-letter promotions, partitioned by
            /// <see cref="Outbox.Tags.EffectKind"/> and <see cref="Tags.FailureReason"/>.</summary>
            public const string PromotionCount = "edict.dead_letter.promotion.count";

            /// <summary>Counter of internal promoter faults that fell through to a
            /// synthetic dead-letter row (an unsupported <c>OutboxEffectKind</c>,
            /// a <c>SendCommand</c> whose command lacks <c>[EdictRouteKey]</c>, or a
            /// forensic body that could not be materialised or JSON-serialised).
            /// Partitioned by <see cref="Tags.PromotionFailureReason"/> and
            /// <see cref="Common.Tags.GrainType"/>. A non-zero rate means the safety net
            /// caught what would otherwise have been a poison-pill reminder loop;
            /// alert on it.</summary>
            public const string PromotionFailureCount = "edict.dead_letter.promotion.failure.count";
        }
    }

    public static class Idempotency
    {
        public static class Tags
        {
            /// <summary>Why a redelivery was suppressed: <see cref="ReasonValues.Window"/>
            /// when the EventId was already in the committed dedup window, or
            /// <see cref="ReasonValues.InFlight"/> when a concurrent delivery of the same
            /// EventId was still in flight on the grain. The set is closed at compile time
            /// so the dimension stays bounded.</summary>
            public const string Reason = "edict.idempotency.dedup.reason";

            /// <summary>The closed allowlist of values for <see cref="Reason"/>.</summary>
            public static class ReasonValues
            {
                public const string Window = "window";
                public const string InFlight = "in_flight";
            }
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
        public const string Recorded = "edict.cmd-recorded";

        /// <summary>Set when the captured context is a fire-and-forget cross-turn dispatch
        /// (a saga's outbox <c>SendCommand</c>), so the receiving <c>edict.command.handle</c>
        /// links back to the send span as a new root instead of nesting under it as a child.</summary>
        public const string CrossTurnLink = "edict.cmd-cross-turn";
    }
}
