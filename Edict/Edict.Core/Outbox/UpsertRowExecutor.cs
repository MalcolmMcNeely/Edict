using Edict.Contracts.Events;
using Edict.Core.TableStorage;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Outbox;

sealed class UpsertRowExecutor(
    Serializer serializer,
    ObjectSerializer rowSerializer,
    RowTypeResolver rowTypeResolver,
    IServiceProvider services) : IOutboxEffectExecutor
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

        // Resolve the row's concrete CLR type from its frozen [Alias] literal
        // and decode straight into that type via ObjectSerializer — skipping
        // the polymorphic Deserialize<object> dispatch the previous codec hop
        // required.
        var rowType = rowTypeResolver.Resolve(effect.RowAlias);
        var row = rowSerializer.Deserialize(effect.RowBytes, rowType);

        var factory = services.GetRequiredService<IEdictTableStoreFactory>();
        await factory.UpsertRowAsync(effect.TableName, effect.PartitionKey, effect.RowKey, row);
    }
}
