using System.Reflection;
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
        EdictEvent edictEvent,
        IEventStreamAccessors accessors,
        Exception exception,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset deadLetteredAt)
    {
        var (streamName, _) = accessors.Resolve(edictEvent);
        var effectTarget = $"{streamName}/{edictEvent.GetType().Name}";
        var payloadJson = JsonSerializer.Serialize(edictEvent, edictEvent.GetType());
        return Compose(entry, effectTarget, payloadJson, exception, sourceGrainKey, sourceGrainType, deadLetteredAt);
    }

    public static EdictDeadLetterRaised Build(
        OutboxEntry entry,
        EdictCommand command,
        string targetGrainType,
        Guid targetGrainKey,
        Exception exception,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset deadLetteredAt)
    {
        var effectTarget = $"{targetGrainType}/{targetGrainKey:D}";
        var payloadJson = JsonSerializer.Serialize(command, command.GetType());
        return Compose(entry, effectTarget, payloadJson, exception, sourceGrainKey, sourceGrainType, deadLetteredAt);
    }

    public static EdictDeadLetterRaised BuildForInvokeHandler(
        OutboxEntry entry,
        EdictEvent edictEvent,
        IEventStreamAccessors accessors,
        Exception exception,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset deadLetteredAt)
    {
        var (streamName, _) = accessors.Resolve(edictEvent);
        var effectTarget = $"{streamName}/{edictEvent.GetType().Name}";
        var payloadJson = JsonSerializer.Serialize(edictEvent, edictEvent.GetType());
        var raised = Compose(entry, effectTarget, payloadJson, exception, sourceGrainKey, sourceGrainType, deadLetteredAt);
        return raised with
        {
            SourceEventType = edictEvent.GetType().FullName,
            SourceEventId = edictEvent.EventId,
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
        string? payloadJson,
        Exception exception,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset deadLetteredAt)
    {
        var effectTarget = $"{effect.TableName}/{effect.PartitionKey}/{effect.RowKey}";
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

    public static bool TryResolveCommandRouteKey(EdictCommand command, out Guid routeKey)
    {
        var routeKeyProp = Array.Find(
            command.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => Attribute.IsDefined(p, typeof(EdictRouteKeyAttribute)));
        if (routeKeyProp is null)
        {
            routeKey = Guid.Empty;
            return false;
        }

        routeKey = (Guid)routeKeyProp.GetValue(command)!;
        return true;
    }
}
