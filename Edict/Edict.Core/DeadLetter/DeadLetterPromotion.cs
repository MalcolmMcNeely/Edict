using System.Reflection;
using System.Text.Json;

using Edict.Contracts.Commands;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;
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
        var raised = Compose(entry, effectTarget, payloadJson, exception, sourceGrainKey, sourceGrainType, deadLetteredAt);
        return raised with { SourceEventId = edictEvent.EventId, ConversationId = edictEvent.ConversationId };
    }

    public static EdictDeadLetterRaised Build(
        OutboxEntry entry,
        EdictCommand command,
        string targetGrainType,
        string targetGrainKey,
        Exception exception,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset deadLetteredAt)
    {
        var effectTarget = $"{targetGrainType}/{targetGrainKey}";
        var payloadJson = JsonSerializer.Serialize(command, command.GetType());
        var raised = Compose(entry, effectTarget, payloadJson, exception, sourceGrainKey, sourceGrainType, deadLetteredAt);
        return raised with { ConversationId = command.ConversationId };
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
            ConversationId = edictEvent.ConversationId,
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
            ? $"{innerStream}/{envelope.InnerEventRouteKey}"
            : nameof(EdictEventEnvelope);
        var raised = Compose(entry, effectTarget, payloadJson: null, exception, sourceGrainKey, sourceGrainType, deadLetteredAt);
        return raised with
        {
            SourceEventId = envelope.EventId,
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
            ? $"{innerStream}/{envelope.InnerEventRouteKey}"
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
            FailureKind = EdictDeadLetterFailureKind.BlobMissing,
            SourceEventId = envelope.EventId,
            Tenant = ResolveTenant(sourceGrainKey),
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
        Tenant = ResolveTenant(sourceGrainKey),
    };

    // The source aggregate's wall, parsed from its own composed grain key: a
    // tenant-scoped key folds "{tenant}|{guid}", a public one is the bare guid.
    // Promote() runs outside the engine's per-group catch, so this must never
    // throw — an unparseable key (a non-Edict source, or a future key shape)
    // degrades to no tag rather than poison-looping the dead-letter reminder.
    internal static EdictTenantId? ResolveTenant(string sourceGrainKey)
    {
        try
        {
            return EdictKeyComposer.Parse(sourceGrainKey).Tenant;
        }
        catch (EdictMalformedRoutedKeyException)
        {
            return null;
        }
    }

    public static bool TryResolveCommandRouteKey(EdictCommand command, out string routeKey)
    {
        var routeKeyProperty = Array.Find(
            command.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => Attribute.IsDefined(property, typeof(EdictRouteKeyAttribute)));
        if (routeKeyProperty is null)
        {
            routeKey = string.Empty;
            return false;
        }

        var value = routeKeyProperty.GetValue(command);
        if (value is Guid guid)
        {
            routeKey = guid.ToString("N");
            return true;
        }

        // A typed route key is a struct wrapping a single Guid; stringify the same
        // bare form the generator emits so the forensic target reads identically.
        var innerGuidProperty = value is null ? null : Array.Find(
            value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.PropertyType == typeof(Guid));
        if (innerGuidProperty?.GetValue(value) is Guid innerGuid)
        {
            routeKey = innerGuid.ToString("N");
            return true;
        }

        routeKey = string.Empty;
        return false;
    }
}
