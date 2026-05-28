using Edict.Contracts.Events;
using Edict.Core.TableStorage;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Outbox;

sealed class UpsertRowExecutor(Serializer serializer, IServiceProvider services) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.UpsertRow;

    public async Task ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task>? deferredDispatch,
        Type? consumerType,
        EdictEvent? liveWireEvent)
    {
        var effect = serializer.Deserialize<UpsertRowEffect>(entry.Payload);

        var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);
        using var activity = EdictDiagnostics.ActivitySource.StartEdictTableUpsert(
            effect.TableName, parentContext);

        // The row is polymorphic-over-object: Orleans round-trips the concrete
        // type via the same TypeConverter that captured its [Alias] at stage.
        var row = serializer.Deserialize<object>(effect.RowBytes);

        var factory = services.GetRequiredService<IEdictTableStoreFactory>();
        await factory.UpsertRowAsync(effect.TableName, effect.PartitionKey, effect.RowKey, row);
    }
}
