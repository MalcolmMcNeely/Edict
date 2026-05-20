using System.Reflection;
using System.Text;
using System.Text.Json;

using Edict.Contracts.Commands;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.Outbox;

namespace Edict.Core.DeadLetter;

/// <summary>
/// Pure module that promotes a failing <see cref="OutboxEntry"/> to an
/// <see cref="EdictDeadLetterRaised"/> event (ADR 0022). No DI, no I/O — owns
/// the effect-kind → <c>EffectTarget</c> mapping, the System.Text.Json
/// serialization of the payload for operator inspection (distinct from the
/// MessagePack wire format per ADR 0007), trace context propagation
/// (ADR 0003), and exception capture. The engine deserializes the entry
/// payload via its existing Orleans serializer and hands the deserialized
/// effect in.
/// </summary>
static class DeadLetterPromotion
{
    public static EdictDeadLetterRaised Build(
        OutboxEntry entry,
        EdictEvent evt,
        Exception exception,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset deadLetteredAt)
    {
        var (streamName, _) = EventStreamAddress.Resolve(evt);
        var effectTarget = $"{streamName}/{evt.GetType().Name}";
        var payloadJson = JsonSerializer.Serialize(evt, evt.GetType());
        return Compose(entry, effectTarget, payloadJson, exception, sourceGrainKey, sourceGrainType, deadLetteredAt);
    }

    public static EdictDeadLetterRaised Build(
        OutboxEntry entry,
        EdictCommand command,
        string targetGrainType,
        Exception exception,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset deadLetteredAt)
    {
        var targetGrainKey = ResolveCommandRouteKey(command);
        var effectTarget = $"{targetGrainType}/{targetGrainKey:D}";
        var payloadJson = JsonSerializer.Serialize(command, command.GetType());
        return Compose(entry, effectTarget, payloadJson, exception, sourceGrainKey, sourceGrainType, deadLetteredAt);
    }

    /// <summary>
    /// Specialised overload for <see cref="OutboxEffectKind.InvokeHandler"/>
    /// promotions (ADR 0023): populates the new
    /// <see cref="EdictDeadLetterRaised.SourceEventType"/> /
    /// <see cref="EdictDeadLetterRaised.SourceEventId"/> fields from the
    /// failing entry's <see cref="EdictEvent"/> payload so an operator can
    /// filter the dead-letter projection by event type without parsing payload
    /// bytes. Existing kinds leave the two fields null.
    /// </summary>
    public static EdictDeadLetterRaised BuildForInvokeHandler(
        OutboxEntry entry,
        EdictEvent evt,
        Exception exception,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset deadLetteredAt)
    {
        var (streamName, _) = EventStreamAddress.Resolve(evt);
        var effectTarget = $"{streamName}/{evt.GetType().Name}";
        var payloadJson = JsonSerializer.Serialize(evt, evt.GetType());
        var raised = Compose(entry, effectTarget, payloadJson, exception, sourceGrainKey, sourceGrainType, deadLetteredAt);
        return raised with
        {
            SourceEventType = evt.GetType().FullName,
            SourceEventId = evt.EventId,
        };
    }

    /// <summary>
    /// Publisher-side specialisation for an oversized event (ADR 0024, slice 4).
    /// The failing Outbox entry's payload is a pointer-bearing
    /// <see cref="EdictEventEnvelope"/> — the inner body is in the claim-check
    /// blob store, not on the wire. The forensic row lifts
    /// <see cref="EdictEventEnvelope.ClaimCheckKey"/> onto the
    /// <see cref="EdictDeadLetterRaised"/> so an operator clicks through to the
    /// blob; <see cref="EdictDeadLetterRaised.PayloadJson"/> stays null because
    /// the >32 KB body never fits into the Azure Table property the dead-letter
    /// projection writes (the failure mode claim-check was designed to prevent).
    /// <see cref="EdictDeadLetterFailureKind.EffectFailure"/> stays the
    /// discriminator — this is still a publisher-side promotion, distinct from
    /// the receiver-side <see cref="EdictDeadLetterFailureKind.BlobMissing"/>.
    /// </summary>
    public static EdictDeadLetterRaised BuildForEnvelopeFailure(
        OutboxEntry entry,
        EdictEventEnvelope envelope,
        Exception exception,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset deadLetteredAt)
    {
        var effectTarget = envelope.InnerEventStreamName is { } innerStream
            ? $"{innerStream}/{envelope.InnerEventRouteKey:D}"
            : nameof(EdictEventEnvelope);
        var raised = Compose(entry, effectTarget, payloadJson: null, exception, sourceGrainKey, sourceGrainType, deadLetteredAt);
        return raised with
        {
            ClaimCheckKey = envelope.ClaimCheckKey,
        };
    }

    public static EdictDeadLetterRaised Build(
        OutboxEntry entry,
        UpsertRowEffect effect,
        Exception exception,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset deadLetteredAt)
    {
        var effectTarget = $"{effect.TableName}/{effect.PartitionKey}/{effect.RowKey}";
        // UpsertRowEffect already carries the row as UTF-8 JSON (the row POCO has
        // no Orleans codec — see UpsertRowEffect). Use it verbatim as display data.
        var payloadJson = effect.RowJson.Length == 0
            ? null
            : Encoding.UTF8.GetString(effect.RowJson);
        return Compose(entry, effectTarget, payloadJson, exception, sourceGrainKey, sourceGrainType, deadLetteredAt);
    }

    /// <summary>
    /// Receiver-side specialisation (ADR 0024, slice 3): builds an
    /// <see cref="EdictDeadLetterRaised"/> for an inbound event whose
    /// claim-check blob could not be fetched after <c>MaxAttempts</c>. There
    /// is no failing <c>OutboxEntry</c> on the receiver path, so the entry-shaped
    /// fields (EntryId, Kind, AttemptCount, TraceParent) come from the envelope
    /// itself rather than a buffered effect. The forensic row carries the
    /// missing key, the inner event's stream/route-key as the EffectTarget, and
    /// <see cref="EdictDeadLetterFailureKind.BlobMissing"/> as the discriminator.
    /// </summary>
    public static EdictDeadLetterRaised BuildForBlobMissing(
        EdictEventEnvelope envelope,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset deadLetteredAt)
    {
        var effectTarget = envelope.InnerEventStreamName is { } innerStream
            ? $"{innerStream}/{envelope.InnerEventRouteKey:D}"
            : nameof(EdictDeadLetterFailureKind.BlobMissing);

        var traceParent = envelope.TraceId is { Length: > 0 } traceId && envelope.SpanId is { Length: > 0 } spanId
            ? $"00-{traceId}-{spanId}-01"
            : null;

        return new EdictDeadLetterRaised
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.PublishEvent.ToString(),
            AttemptCount = 0,
            DeadLetteredAt = deadLetteredAt,
            SourceGrainKey = sourceGrainKey,
            SourceGrainType = sourceGrainType,
            EffectTarget = effectTarget,
            TraceParent = traceParent,
            ExceptionType = nameof(KeyNotFoundException),
            Reason = $"Claim-check blob '{envelope.ClaimCheckKey}' was not found.",
            PayloadJson = null,
            ClaimCheckKey = envelope.ClaimCheckKey,
            FailureKind = EdictDeadLetterFailureKind.BlobMissing,
        };
    }

    static EdictDeadLetterRaised Compose(
        OutboxEntry entry,
        string effectTarget,
        string? payloadJson,
        Exception exception,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset deadLetteredAt) => new()
    {
        EntryId = entry.EntryId,
        Kind = entry.Kind.ToString(),
        AttemptCount = entry.AttemptCount,
        DeadLetteredAt = deadLetteredAt,
        SourceGrainKey = sourceGrainKey,
        SourceGrainType = sourceGrainType,
        EffectTarget = effectTarget,
        TraceParent = entry.TraceParent,
        ExceptionType = exception.GetType().FullName,
        Reason = exception.Message,
        PayloadJson = payloadJson,
    };

    static Guid ResolveCommandRouteKey(EdictCommand command)
    {
        var routeKeyProp = Array.Find(
            command.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => Attribute.IsDefined(p, typeof(EdictRouteKeyAttribute)))
            ?? throw new InvalidOperationException(
                $"Command {command.GetType().Name} is missing a [EdictRouteKey] Guid property (ADR 0011).");

        return (Guid)routeKeyProp.GetValue(command)!;
    }
}
