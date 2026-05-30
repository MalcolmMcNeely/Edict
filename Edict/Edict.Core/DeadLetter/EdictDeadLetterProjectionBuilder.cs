using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.Projections;
using Edict.Core.TableStorage;
using Edict.Telemetry;

using Orleans;

namespace Edict.Core.DeadLetter;

[ImplicitStreamSubscription("edict-dead-letter")]
internal sealed class EdictDeadLetterProjectionBuilder(IEdictTableStoreFactory storeFactory)
    : EdictTableProjectionBuilder<EdictDeadLetterEntry>(storeFactory)
{
    protected override string TableName => EdictDeadLetterTable.Name;

    protected override string DefaultPartitionKey => EdictDeadLetterTable.Name;

    protected override string GetRowKey(EdictEvent edictEvent) =>
        edictEvent switch
        {
            EdictDeadLetterRaised raised => raised.EntryId.ToString("N"),
            _ => this.GetPrimaryKey().ToString(),
        };

    public Task HandleAsync(EdictDeadLetterRaised raised)
    {
        CurrentRow = new EdictDeadLetterEntry
        {
            EntryId = raised.EntryId,
            Kind = raised.Kind,
            AttemptCount = raised.AttemptCount,
            DeadLetteredAt = raised.DeadLetteredAt,
            SourceGrainKey = raised.SourceGrainKey,
            SourceGrainType = raised.SourceGrainType,
            EffectTarget = raised.EffectTarget,
            TraceParent = raised.TraceParent,
            ExceptionType = raised.ExceptionType,
            Reason = raised.Reason,
            PayloadJson = raised.PayloadJson,
            SourceEventType = raised.SourceEventType,
            SourceEventId = raised.SourceEventId,
            ClaimCheckKey = raised.ClaimCheckKey,
            FailureKind = raised.FailureKind,
        };
        return Task.CompletedTask;
    }

    protected override async Task<bool> DispatchAsync(EdictEvent edictEvent)
    {
        switch (edictEvent)
        {
            case EdictDeadLetterRaised raised:
            {
                var parentContext = ActivityExtensions.RestoreFromStrings(edictEvent.TraceId, edictEvent.SpanId, edictEvent.TraceState);
                using var span = EdictDiagnostics.ActivitySource.StartEdictEventHandle(
                    nameof(EdictDeadLetterRaised), parentContext);
                await DispatchEventAsync(raised, HandleAsync);
                return true;
            }
            default:
                return false;
        }
    }
}
