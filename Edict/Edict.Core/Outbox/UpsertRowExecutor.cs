using System.Text.Json;

using Edict.Contracts.Events;
using Edict.Core.TableStorage;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Outbox;

/// <summary>
/// Drains a <see cref="OutboxEffectKind.UpsertRow"/> entry: reconstitute the
/// captured row and write it to the Table Projection store via the non-generic
/// <see cref="IEdictTableStoreFactory.UpsertRowAsync"/> seam (ADR 0018). The
/// write is a full-row replace keyed by pk/rk, so at-least-once redelivery of
/// the effect does not double-apply — this is the mechanism that closes ADR
/// 0012's double-apply gap. The factory is resolved lazily so a host that wires
/// the Outbox but has no Table Projection (no
/// <see cref="IEdictTableStoreFactory"/> registered) still constructs the
/// engine. Bare-named — no consumer types it.
/// <para>
/// ADR 0027: the row type is captured as its frozen <c>[Alias]</c> literal and
/// resolved here via <see cref="TypeConverter.Parse"/> against the Orleans
/// manifest, so a consumer who renames the row POCO class but preserves the
/// alias has no in-flight entries dead-lettered — superseding the previous
/// assembly-qualified-name lookup which broke on rename or move.
/// </para>
/// </summary>
sealed class UpsertRowExecutor(
    Serializer serializer,
    Orleans.Serialization.TypeSystem.TypeConverter typeConverter,
    IServiceProvider services) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.UpsertRow;

    public async Task ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task>? deferredDispatch,
        Type? consumerType)
    {
        var effect = serializer.Deserialize<UpsertRowEffect>(entry.Payload);

        var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);
        using var activity = EdictDiagnostics.ActivitySource.StartEdictTableUpsert(
            effect.TableName, parentContext);

        Type rowType;
        try
        {
            rowType = typeConverter.Parse(effect.RowAlias);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"UpsertRow effect references an unresolvable row alias '{effect.RowAlias}'.", ex);
        }

        var row = JsonSerializer.Deserialize(effect.RowJson, rowType)
            ?? throw new InvalidOperationException(
                $"UpsertRow effect row for table '{effect.TableName}' deserialized to null.");

        var factory = services.GetRequiredService<IEdictTableStoreFactory>();
        await factory.UpsertRowAsync(effect.TableName, effect.PartitionKey, effect.RowKey, row);
    }
}
