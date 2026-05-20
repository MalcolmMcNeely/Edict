using System.Text.Json;

using Edict.Core.TableStorage;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

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
/// </summary>
sealed class UpsertRowExecutor(Serializer serializer, IServiceProvider services) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.UpsertRow;

    public async Task ExecuteAsync(OutboxEntry entry, IOutboxHost host)
    {
        var effect = serializer.Deserialize<UpsertRowEffect>(entry.Payload);

        var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);
        using var activity = EdictDiagnostics.ActivitySource.StartEdictTableUpsert(
            effect.TableName, parentContext);

        var rowType = Type.GetType(effect.RowTypeName)
            ?? throw new InvalidOperationException(
                $"UpsertRow effect references an unresolvable row type '{effect.RowTypeName}'.");

        var row = JsonSerializer.Deserialize(effect.RowJson, rowType)
            ?? throw new InvalidOperationException(
                $"UpsertRow effect row for table '{effect.TableName}' deserialized to null.");

        var factory = services.GetRequiredService<IEdictTableStoreFactory>();
        await factory.UpsertRowAsync(effect.TableName, effect.PartitionKey, effect.RowKey, row);
    }
}
