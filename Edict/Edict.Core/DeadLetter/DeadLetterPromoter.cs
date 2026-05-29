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
        // Promote() runs outside the engine's per-group catch. A throw here
        // propagates up the grain drain, skips the state write, leaves the
        // failed entry Pending, and the next reminder fires the same throw —
        // a poison-pill reminder loop. If the cause is itself unrepresentable
        // (unknown effect kind, command missing [EdictRouteKey]), log + count +
        // emit a synthetic row marked with a string-marker exception type
        // instead of throwing.
        var raised = failed.Kind switch
        {
            OutboxEffectKind.PublishEvent => BuildFromPublishEvent(failed, exception, sourceGrainKey, sourceGrainType, now),
            OutboxEffectKind.SendCommand => BuildFromSendCommand(failed, exception, sourceGrainKey, sourceGrainType, now),
            OutboxEffectKind.UpsertRow => BuildFromUpsertRow(failed, exception, sourceGrainKey, sourceGrainType, now),
            OutboxEffectKind.InvokeHandler => BuildFromInvokeHandler(failed, exception, sourceGrainKey, sourceGrainType, now),
            _ => BuildForUnsupportedKind(failed, exception, sourceGrainKey, sourceGrainType, now),
        };

        PromotionCount.Add(1,
            new KeyValuePair<string, object?>(SemanticConventions.Outbox.Tags.EffectKind, failed.Kind.ToString()),
            new KeyValuePair<string, object?>(SemanticConventions.DeadLetter.Tags.FailureReason, DeadLetterFailureClassifier.Classify(exception)),
            new KeyValuePair<string, object?>(SemanticConventions.Common.Tags.GrainType, sourceGrainType));

        return new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.PublishEvent,
            Payload = serializer.SerializeToArray<EdictEvent>(raised),
            TraceParent = failed.TraceParent,
            TraceState = failed.TraceState,
            AttemptCount = 0,
            NextAttemptUtc = now,
        };
    }

    EdictDeadLetterRaised BuildFromPublishEvent(
        OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now)
    {
        var edictEvent = serializer.Deserialize<EdictEvent>(failed.Payload);
        // An oversized event rides as a pointer-bearing envelope on the
        // wire. Lift the pointer onto the forensic row instead of trying
        // to JSON-serialise the body into a 32 KB Azure Table property —
        // the very failure mode claim-check exists to avoid.
        if (edictEvent is EdictEventEnvelope { ClaimCheckKey: { Length: > 0 } } envelope)
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

    EdictDeadLetterRaised BuildFromUpsertRow(
        OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now)
    {
        var effect = serializer.Deserialize<UpsertRowEffect>(failed.Payload);
        var row = serializer.Deserialize<object>(effect.RowBytes);
        var payloadJson = JsonSerializer.Serialize(row, row.GetType());
        return DeadLetterPromotion.Build(failed, effect, payloadJson, exception, sourceGrainKey, sourceGrainType, now);
    }

    EdictDeadLetterRaised BuildFromInvokeHandler(
        OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now)
    {
        var edictEvent = serializer.Deserialize<EdictEvent>(failed.Payload);
        // An InvokeHandler entry whose payload is a pointer-bearing envelope
        // represents a receiver-side missing-blob exhaustion — route through
        // the BlobMissing failure-kind mapping so the forensic row carries
        // the claim-check key and the inline-payload envelope path stays
        // unchanged.
        if (edictEvent is EdictEventEnvelope { ClaimCheckKey: { Length: > 0 } } pointer)
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
