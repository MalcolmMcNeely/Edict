using System.Diagnostics.Metrics;
using System.Text.Json;

using Edict.Contracts.Commands;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Orleans.Serialization;

namespace Edict.Core.DeadLetter;

sealed class DeadLetterPromoter(
    Serializer serializer,
    ObjectSerializer rowSerializer,
    RowTypeResolver rowTypeResolver,
    IEventStreamAccessors accessors,
    IServiceProvider services,
    ILogger<DeadLetterPromoter> logger)
    : IDeadLetterPromoter
{
    static readonly Counter<long> PromotionCount = EdictDiagnostics.Meter.CreateCounter<long>(
        SemanticConventions.DeadLetter.Meters.PromotionCount);
    static readonly Counter<long> PromotionFailureCount = EdictDiagnostics.Meter.CreateCounter<long>(
        SemanticConventions.DeadLetter.Meters.PromotionFailureCount);

    public OutboxEntry Promote(
        OutboxEntry failed,
        Exception exception,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset now)
    {
        // The promotion runs in a decoupled drain turn, not the command turn that
        // produced the failure, so its span links back to the entry's persisted
        // context rather than nesting under it — an operator pivots from the
        // promoted dead-letter to the originating command trace. Start / SetTag /
        // Dispose are non-throwing, so the span is safe to open here outside the
        // body-materialise try/catch on the safety-net path.
        using var activity = EdictDiagnostics.ActivitySource.StartEdictDeadLetterPromote(
            sourceGrainType, ActivityExtensions.BuildLink(failed.TraceParent, failed.TraceState));
        activity?.SetTag(SemanticConventions.Common.Tags.GrainType, sourceGrainType);
        activity?.SetTag(SemanticConventions.Outbox.Tags.EffectKind, failed.Kind.ToString());

        // Promote() runs outside the engine's per-group catch. A throw here
        // propagates up the grain drain, skips the state write, leaves the
        // failed entry Pending, and the next reminder fires the same throw —
        // a poison-pill reminder loop. The named-cause arms (unknown effect
        // kind, command missing [EdictRouteKey]) already log + count + return a
        // synthetic marker row rather than throw. The outer catch is the
        // backstop for the unnamed cause: a body that cannot be materialised or
        // JSON-serialised — a renamed/removed row type, or a consumer payload
        // that defeats the serializer — which must degrade, never escape.
        EdictDeadLetterRaised raised;
        try
        {
            raised = failed.Kind switch
            {
                OutboxEffectKind.PublishEvent => BuildFromPublishEvent(failed, exception, sourceGrainKey, sourceGrainType, now),
                OutboxEffectKind.SendCommand => BuildFromSendCommand(failed, exception, sourceGrainKey, sourceGrainType, now),
                OutboxEffectKind.UpsertRow => BuildFromUpsertRow(failed, exception, sourceGrainKey, sourceGrainType, now),
                OutboxEffectKind.InvokeHandler => BuildFromInvokeHandler(failed, exception, sourceGrainKey, sourceGrainType, now),
                _ => BuildForUnsupportedKind(failed, exception, sourceGrainKey, sourceGrainType, now),
            };
        }
        catch (Exception promotionException)
        {
            raised = BuildForSerializationFailure(failed, promotionException, sourceGrainKey, sourceGrainType, now);
        }

        var failureReason = DeadLetterFailureClassifier.Classify(exception, services.GetServices<IDeadLetterFaultClassifier>());
        activity?.SetTag(SemanticConventions.DeadLetter.Tags.FailureReason, failureReason);

        PromotionCount.Add(1,
            new KeyValuePair<string, object?>(SemanticConventions.Outbox.Tags.EffectKind, failed.Kind.ToString()),
            new KeyValuePair<string, object?>(SemanticConventions.DeadLetter.Tags.FailureReason, failureReason),
            new KeyValuePair<string, object?>(SemanticConventions.Common.Tags.GrainType, sourceGrainType));

        // The forensic notification is its own event with its own identity. The
        // drain no longer mints one, so stamp it here at the single choke point
        // that covers every promoter path. A fresh id (never the source event's)
        // keeps two distinct failures of one source event as two dead-letter
        // rows. Guid.NewGuid cannot throw, so the safety-net no-throw rule holds.
        return new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.PublishEvent,
            Payload = serializer.SerializeToArray<EdictEvent>(raised with { EventId = Guid.NewGuid() }),
            TraceParent = failed.TraceParent,
            TraceState = failed.TraceState,
            AttemptCount = 0,
            NextAttemptUtc = now,
        };
    }

    public OutboxEntry PromoteScheduleTimeout(
        string scheduleMessageType,
        string sourceGrainKey,
        string sourceGrainType,
        string? traceParent,
        string? traceState,
        DateTimeOffset now)
    {
        // No failing OutboxEntry to deserialise here, so nothing in this method can
        // throw — it builds the forensic row from the supplied strings only. The
        // marker type is named, never instantiated, mirroring the saga timeout row
        // but routed through the promoter as the schedule lifecycle's single
        // dead-letter choke point.
        using var activity = EdictDiagnostics.ActivitySource.StartEdictDeadLetterPromote(
            sourceGrainType, ActivityExtensions.BuildLink(traceParent, traceState));
        activity?.SetTag(SemanticConventions.Common.Tags.GrainType, sourceGrainType);
        activity?.SetTag(SemanticConventions.Outbox.Tags.EffectKind, OutboxEffectKind.PublishEvent.ToString());
        activity?.SetTag(
            SemanticConventions.DeadLetter.Tags.FailureReason,
            SemanticConventions.DeadLetter.Tags.FailureReasonValues.ScheduleTimeout);

        var raised = new EdictDeadLetterRaised
        {
            EventId = Guid.NewGuid(),
            EntryId = Guid.NewGuid(),
            Kind = SemanticConventions.DeadLetter.Tags.FailureReasonValues.ScheduleTimeout,
            AttemptCount = 0,
            DeadLetteredAt = now,
            SourceGrainKey = sourceGrainKey,
            SourceGrainType = sourceGrainType,
            EffectTarget = sourceGrainType,
            TraceParent = traceParent,
            ExceptionType = nameof(EdictScheduleTimeoutException),
            Reason =
                $"Schedule message '{scheduleMessageType}' hit its timeout cap with no OnScheduleTimeoutAsync hook; "
                + "the timeout was dead-lettered.",
        };

        PromotionCount.Add(1,
            new KeyValuePair<string, object?>(
                SemanticConventions.Outbox.Tags.EffectKind, OutboxEffectKind.PublishEvent.ToString()),
            new KeyValuePair<string, object?>(
                SemanticConventions.DeadLetter.Tags.FailureReason,
                SemanticConventions.DeadLetter.Tags.FailureReasonValues.ScheduleTimeout),
            new KeyValuePair<string, object?>(SemanticConventions.Common.Tags.GrainType, sourceGrainType));

        return new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.PublishEvent,
            Payload = serializer.SerializeToArray<EdictEvent>(raised),
            TraceParent = traceParent,
            TraceState = traceState,
            AttemptCount = 0,
            NextAttemptUtc = now,
        };
    }

    EdictDeadLetterRaised BuildFromPublishEvent(
        OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now)
    {
        var edictEvent = serializer.Deserialize<EdictEvent>(failed.Payload);
        // An oversized event rides as a pointer-bearing envelope on the
        // wire (InlinePayload null). Carry its EventId onto the forensic row
        // instead of trying to JSON-serialise the body into a 32 KB Azure Table
        // property — the very failure mode claim-check exists to avoid.
        if (edictEvent is EdictEventEnvelope { InlinePayload: null } envelope)
        {
            return DeadLetterPromotion.BuildForEnvelopeFailure(
                failed, envelope, exception, sourceGrainKey, sourceGrainType, now);
        }
        return DeadLetterPromotion.Build(failed, edictEvent, accessors, exception, sourceGrainKey, sourceGrainType, now);
    }

    EdictDeadLetterRaised BuildFromSendCommand(
        OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now)
    {
        var command = serializer.Deserialize<EdictCommand>(failed.Payload);
        var routes = services.GetRequiredService<CommandRouteResolver>();
        var targetGrainType = routes.GetRoute(command).GrainClassName;
        if (DeadLetterPromotion.TryResolveCommandRouteKey(command, out var targetGrainKey))
        {
            return DeadLetterPromotion.Build(failed, command, targetGrainType, targetGrainKey, exception, sourceGrainKey, sourceGrainType, now);
        }

        logger.LogWarning(
            "Dead-letter promoter could not resolve [EdictRouteKey] on command '{CommandType}' for source grain '{SourceGrainType}'. Emitting synthetic dead-letter row with RouteKey=Guid.Empty.",
            command.GetType().FullName, sourceGrainType);
        PromotionFailureCount.Add(1,
            new KeyValuePair<string, object?>(
                SemanticConventions.DeadLetter.Tags.PromotionFailureReason,
                SemanticConventions.DeadLetter.Tags.PromotionFailureReasonValues.MissingRouteKey),
            new KeyValuePair<string, object?>(
                SemanticConventions.Common.Tags.GrainType, sourceGrainType));
        var raised = DeadLetterPromotion.Build(failed, command, targetGrainType, Guid.Empty, exception, sourceGrainKey, sourceGrainType, now);
        return raised with { ExceptionType = nameof(EdictMissingRouteKeyException) };
    }

    EdictDeadLetterRaised BuildForUnsupportedKind(
        OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now)
    {
        logger.LogWarning(
            "Dead-letter promoter received an unsupported OutboxEffectKind '{EffectKind}' from source grain '{SourceGrainType}'. Emitting synthetic dead-letter row.",
            failed.Kind, sourceGrainType);
        PromotionFailureCount.Add(1,
            new KeyValuePair<string, object?>(
                SemanticConventions.DeadLetter.Tags.PromotionFailureReason,
                SemanticConventions.DeadLetter.Tags.PromotionFailureReasonValues.UnsupportedKind),
            new KeyValuePair<string, object?>(
                SemanticConventions.Common.Tags.GrainType, sourceGrainType));
        return new EdictDeadLetterRaised
        {
            EntryId = failed.EntryId,
            Kind = failed.Kind.ToString(),
            AttemptCount = failed.AttemptCount,
            DeadLetteredAt = now,
            SourceGrainKey = sourceGrainKey,
            SourceGrainType = sourceGrainType,
            EffectTarget = nameof(EdictUnsupportedEffectKindException),
            TraceParent = failed.TraceParent,
            ExceptionType = nameof(EdictUnsupportedEffectKindException),
            Reason = $"Unsupported OutboxEffectKind ordinal {(int)failed.Kind}.",
        };
    }

    EdictDeadLetterRaised BuildForSerializationFailure(
        OutboxEntry failed, Exception promotionException, string sourceGrainKey, string sourceGrainType, DateTimeOffset now)
    {
        logger.LogWarning(
            promotionException,
            "Dead-letter promoter could not materialise the forensic body for a '{EffectKind}' effect from source grain '{SourceGrainType}'. Emitting synthetic dead-letter row without a payload snapshot.",
            failed.Kind, sourceGrainType);
        PromotionFailureCount.Add(1,
            new KeyValuePair<string, object?>(
                SemanticConventions.DeadLetter.Tags.PromotionFailureReason,
                SemanticConventions.DeadLetter.Tags.PromotionFailureReasonValues.SerializationFailure),
            new KeyValuePair<string, object?>(
                SemanticConventions.Common.Tags.GrainType, sourceGrainType));
        return new EdictDeadLetterRaised
        {
            EntryId = failed.EntryId,
            Kind = failed.Kind.ToString(),
            AttemptCount = failed.AttemptCount,
            DeadLetteredAt = now,
            SourceGrainKey = sourceGrainKey,
            SourceGrainType = sourceGrainType,
            EffectTarget = nameof(EdictPromotionSerializationException),
            TraceParent = failed.TraceParent,
            ExceptionType = nameof(EdictPromotionSerializationException),
            Reason = promotionException.Message,
            PayloadJson = null,
        };
    }

    EdictDeadLetterRaised BuildFromUpsertRow(
        OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now)
    {
        var effect = serializer.Deserialize<UpsertRowEffect>(failed.Payload);
        // Resolve the row's concrete type from its frozen [Alias] literal, the
        // same decode the UpsertRowExecutor uses — so a row POCO rename that
        // preserves the alias survives here too, instead of poison-looping the
        // reminder via a Deserialize<object> type-id miss.
        var rowType = rowTypeResolver.Resolve(effect.RowAlias);
        var row = rowSerializer.Deserialize(effect.RowBytes, rowType);
        var payloadJson = JsonSerializer.Serialize(row, rowType);
        return DeadLetterPromotion.Build(failed, effect, payloadJson, exception, sourceGrainKey, sourceGrainType, now);
    }

    EdictDeadLetterRaised BuildFromInvokeHandler(
        OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now)
    {
        var edictEvent = serializer.Deserialize<EdictEvent>(failed.Payload);
        // An InvokeHandler entry whose payload is a pointer-bearing envelope
        // (InlinePayload null) represents a receiver-side missing-blob
        // exhaustion — route through the BlobMissing failure-kind mapping so the
        // forensic row carries the envelope's EventId and the inline-payload
        // envelope path stays unchanged.
        if (edictEvent is EdictEventEnvelope { InlinePayload: null } pointer)
        {
            return DeadLetterPromotion.BuildForBlobMissing(
                failed, pointer, exception, sourceGrainKey, sourceGrainType, now);
        }
        // Inline-payload envelopes carry the inner event in their bytes — the
        // executor's unwrap would have materialised it before deferredDispatch
        // ran, so a failure here is a Handle-side throw against the inner
        // event. Treat it the same as a concrete-event InvokeHandler failure.
        if (edictEvent is EdictEventEnvelope { InlinePayload: { Length: > 0 } innerBytes })
        {
            var inner = serializer.Deserialize<EdictEvent>(innerBytes);
            return DeadLetterPromotion.BuildForInvokeHandler(failed, inner, accessors, exception, sourceGrainKey, sourceGrainType, now);
        }
        return DeadLetterPromotion.BuildForInvokeHandler(failed, edictEvent, accessors, exception, sourceGrainKey, sourceGrainType, now);
    }
}
