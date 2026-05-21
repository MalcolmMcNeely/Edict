using System.Reflection;
using System.Text;
using System.Text.Json;

using Edict.Contracts.Commands;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.Outbox;

namespace Edict.Core.DeadLetter;

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

    public static EdictDeadLetterRaised BuildForBlobMissing(
        OutboxEntry entry,
        EdictEventEnvelope envelope,
        Exception exception,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset deadLetteredAt)
    {
        var effectTarget = envelope.InnerEventStreamName is { } innerStream
            ? $"{innerStream}/{envelope.InnerEventRouteKey:D}"
            : nameof(EdictDeadLetterFailureKind.BlobMissing);

        return new EdictDeadLetterRaised
        {
            EntryId = entry.EntryId,
            Kind = entry.Kind.ToString(),
            AttemptCount = entry.AttemptCount,
            DeadLetteredAt = deadLetteredAt,
            SourceGrainKey = sourceGrainKey,
            SourceGrainType = sourceGrainType,
            EffectTarget = effectTarget,
            TraceParent = entry.TraceParent,
            ExceptionType = exception.GetType().FullName ?? nameof(KeyNotFoundException),
            Reason = exception.Message,
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
                $"Command {command.GetType().Name} is missing a [EdictRouteKey] Guid property.");

        return (Guid)routeKeyProp.GetValue(command)!;
    }
}
