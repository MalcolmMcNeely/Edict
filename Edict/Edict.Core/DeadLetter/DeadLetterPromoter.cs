using Edict.Contracts.Commands;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.Commands;
using Edict.Core.Outbox;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Core.DeadLetter;

sealed class DeadLetterPromoter(Serializer serializer, IEventStreamAccessors accessors, IServiceProvider services)
    : IDeadLetterPromoter
{
    public OutboxEntry Promote(
        OutboxEntry failed,
        Exception exception,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset now)
    {
        var raised = failed.Kind switch
        {
            OutboxEffectKind.PublishEvent => BuildFromPublishEvent(failed, exception, sourceGrainKey, sourceGrainType, now),
            OutboxEffectKind.SendCommand => BuildFromSendCommand(failed, exception, sourceGrainKey, sourceGrainType, now),
            OutboxEffectKind.UpsertRow => BuildFromUpsertRow(failed, exception, sourceGrainKey, sourceGrainType, now),
            OutboxEffectKind.InvokeHandler => BuildFromInvokeHandler(failed, exception, sourceGrainKey, sourceGrainType, now),
            _ => throw new InvalidOperationException($"Unsupported effect kind '{failed.Kind}'."),
        };

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
        var evt = serializer.Deserialize<EdictEvent>(failed.Payload);
        // An oversized event rides as a pointer-bearing envelope on the
        // wire. Lift the pointer onto the forensic row instead of trying
        // to JSON-serialise the body into a 32 KB Azure Table property —
        // the very failure mode claim-check exists to avoid.
        if (evt is EdictEventEnvelope { ClaimCheckKey: { Length: > 0 } } envelope)
        {
            return DeadLetterPromotion.BuildForEnvelopeFailure(
                failed, envelope, exception, sourceGrainKey, sourceGrainType, now);
        }
        return DeadLetterPromotion.Build(failed, evt, accessors, exception, sourceGrainKey, sourceGrainType, now);
    }

    EdictDeadLetterRaised BuildFromSendCommand(
        OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now)
    {
        var command = serializer.Deserialize<EdictCommand>(failed.Payload);
        var routes = services.GetRequiredService<CommandRouteResolver>();
        var targetGrainType = routes.GetRoute(command).GrainClassName;
        return DeadLetterPromotion.Build(failed, command, targetGrainType, exception, sourceGrainKey, sourceGrainType, now);
    }

    EdictDeadLetterRaised BuildFromUpsertRow(
        OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now)
    {
        var effect = serializer.Deserialize<UpsertRowEffect>(failed.Payload);
        var row = serializer.Deserialize<object>(effect.RowBytes);
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(row, row.GetType());
        return DeadLetterPromotion.Build(failed, effect, payloadJson, exception, sourceGrainKey, sourceGrainType, now);
    }

    EdictDeadLetterRaised BuildFromInvokeHandler(
        OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now)
    {
        var evt = serializer.Deserialize<EdictEvent>(failed.Payload);
        // An InvokeHandler entry whose payload is a pointer-bearing envelope
        // represents a receiver-side missing-blob exhaustion — route through
        // the BlobMissing failure-kind mapping so the forensic row carries
        // the claim-check key and the inline-payload envelope path stays
        // unchanged.
        if (evt is EdictEventEnvelope { ClaimCheckKey: { Length: > 0 } } pointer)
        {
            return DeadLetterPromotion.BuildForBlobMissing(
                failed, pointer, exception, sourceGrainKey, sourceGrainType, now);
        }
        // Inline-payload envelopes carry the inner event in their bytes — the
        // executor's unwrap would have materialised it before deferredDispatch
        // ran, so a failure here is a Handle-side throw against the inner
        // event. Treat it the same as a concrete-event InvokeHandler failure.
        if (evt is EdictEventEnvelope { InlinePayload: { Length: > 0 } innerBytes })
        {
            var inner = serializer.Deserialize<EdictEvent>(innerBytes);
            return DeadLetterPromotion.BuildForInvokeHandler(failed, inner, accessors, exception, sourceGrainKey, sourceGrainType, now);
        }
        return DeadLetterPromotion.BuildForInvokeHandler(failed, evt, accessors, exception, sourceGrainKey, sourceGrainType, now);
    }
}
