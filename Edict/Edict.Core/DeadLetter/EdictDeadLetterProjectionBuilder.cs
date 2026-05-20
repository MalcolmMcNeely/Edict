using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.Projections;
using Edict.Core.TableStorage;
using Edict.Telemetry;

using Orleans;

namespace Edict.Core.DeadLetter;

/// <summary>
/// Built-in singleton table projection for the dead-letter pivot (ADR 0022).
/// Consumes the <c>edict-dead-letter</c> stream — every
/// <see cref="EdictDeadLetterRaised"/> the engine emits — and upserts an
/// <see cref="EdictDeadLetterEntry"/> row into the fleet-wide
/// <see cref="TableName"/> table under the constant
/// <see cref="DeadLetterPartition"/> partition (matching the singleton-grain
/// choice; a future fanned-out roll-up can change this without consumer
/// migration). RowKey = <c>EntryId.ToString("N")</c> so every dead-lettered
/// effect produces a distinct row inside that one partition.
/// <para>
/// Auto-wired by <c>AddEdict()</c> — consumers do not need to register either
/// the grain or its repository. Hand-authored (no generator) because it lives in
/// the framework assembly; the source-generator pipeline only runs over consumer
/// projects.
/// </para>
/// </summary>
[ImplicitStreamSubscription("edict-dead-letter")]
public sealed class EdictDeadLetterProjectionBuilder(IEdictTableStoreFactory storeFactory)
    : EdictTableProjectionBuilder<EdictDeadLetterEntry>(storeFactory)
{
    /// <summary>
    /// The single partition every dead-letter row is written to (ADR 0022). All
    /// fleet-wide reads scan this partition.
    /// </summary>
    public const string DeadLetterPartition = "deadletter";

    /// <inheritdoc />
    protected override string TableName => DeadLetterPartition;

    /// <inheritdoc />
    protected override string DefaultPartitionKey => DeadLetterPartition;

    /// <inheritdoc />
    protected override string GetRowKey(EdictEvent evt) =>
        evt switch
        {
            EdictDeadLetterRaised raised => raised.EntryId.ToString("N"),
            _ => this.GetPrimaryKey().ToString(),
        };

    public Task Handle(EdictDeadLetterRaised raised)
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
        };
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task<bool> DispatchAsync(EdictEvent evt)
    {
        switch (evt)
        {
            case EdictDeadLetterRaised raised:
            {
                var parentContext = ActivityExtensions.RestoreFromStrings(evt.TraceId, evt.SpanId, evt.TraceState);
                using var span = EdictDiagnostics.ActivitySource.StartEdictEventHandle(
                    nameof(EdictDeadLetterRaised), parentContext);
                await DispatchEventAsync(raised, Handle);
                return true;
            }
            default:
                return false;
        }
    }
}
