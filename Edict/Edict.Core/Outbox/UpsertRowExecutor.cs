using System.Text.Json;

using Edict.Contracts.Events;
using Edict.Core.TableStorage;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Outbox;

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
