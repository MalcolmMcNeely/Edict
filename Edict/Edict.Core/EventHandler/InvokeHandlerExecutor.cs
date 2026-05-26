using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.Outbox;
using Edict.Telemetry;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.EventHandler;

sealed class InvokeHandlerExecutor(Serializer serializer, ClaimCheckUnwrap unwrap) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.InvokeHandler;

    public async Task ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task>? deferredDispatch,
        Type? consumerType,
        EdictEvent? liveWireEvent)
    {
        if (deferredDispatch is null)
        {
            throw new NotSupportedException(
                "InvokeHandler executor invoked on a host that does not wire deferred dispatch.");
        }

        var staged = serializer.Deserialize<EdictEvent>(entry.Payload);
        var materialised = await unwrap.ApplyAsync(
            staged, consumerType ?? typeof(object), CancellationToken.None);

        var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);
        using var span = EdictDiagnostics.ActivitySource.StartEdictEventHandle(
            materialised.GetType().Name, parentContext);

        await deferredDispatch(materialised);
    }
}
