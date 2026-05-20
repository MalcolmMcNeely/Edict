using Edict.Contracts.Commands;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.Commands;
using Edict.Core.Outbox;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Core.DeadLetter;

/// <summary>
/// Default <see cref="IDeadLetterPromoter"/> implementation (ADR 0022). Owns
/// the Orleans-serializer hop needed to read the failing entry's payload and
/// — only when promoting a <see cref="OutboxEffectKind.SendCommand"/> entry —
/// the <see cref="CommandRouteResolver"/> hop needed to resolve the target
/// grain class name. The resolver is fetched lazily via
/// <see cref="IServiceProvider"/> so hosts that wire the Outbox without a
/// route map (the host-plumbing fixtures, for example) still construct the
/// engine cleanly. Delegates the pure mapping to
/// <see cref="DeadLetterPromotion.Build(OutboxEntry, EdictEvent, Exception, string, string, DateTimeOffset)"/>
/// and its peers, then re-serialises the resulting
/// <see cref="EdictDeadLetterRaised"/> as a new
/// <see cref="OutboxEffectKind.PublishEvent"/> entry the engine appends at
/// the FIFO tail. Bare-named — no consumer types it.
/// </summary>
sealed class DeadLetterPromoter(Serializer serializer, IServiceProvider services)
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
        return DeadLetterPromotion.Build(failed, evt, exception, sourceGrainKey, sourceGrainType, now);
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
        return DeadLetterPromotion.Build(failed, effect, exception, sourceGrainKey, sourceGrainType, now);
    }

    public OutboxEntry PromoteBlobMissing(
        EdictEventEnvelope envelope,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset now)
    {
        var raised = DeadLetterPromotion.BuildForBlobMissing(envelope, sourceGrainKey, sourceGrainType, now);
        var traceParent = envelope.TraceId is { Length: > 0 } traceId && envelope.SpanId is { Length: > 0 } spanId
            ? $"00-{traceId}-{spanId}-01"
            : null;

        return new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.PublishEvent,
            Payload = serializer.SerializeToArray<EdictEvent>(raised),
            TraceParent = traceParent,
            TraceState = envelope.TraceState,
            AttemptCount = 0,
            NextAttemptUtc = now,
        };
    }

    EdictDeadLetterRaised BuildFromInvokeHandler(
        OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now)
    {
        var evt = serializer.Deserialize<EdictEvent>(failed.Payload);
        return DeadLetterPromotion.BuildForInvokeHandler(failed, evt, exception, sourceGrainKey, sourceGrainType, now);
    }
}
