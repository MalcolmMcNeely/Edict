using Edict.Contracts.Events;
using Edict.Core.Outbox;
using Edict.Telemetry;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.EventHandler;

/// <summary>
/// Drains a <see cref="OutboxEffectKind.InvokeHandler"/> entry (ADR 0023):
/// deserialise the buffered <see cref="EdictEvent"/>, restore the captured
/// <c>traceparent</c> so the deferred invocation span nests under the publish
/// span as parent-child even when backoff defers the call across the stream
/// hop (ADR 0003), and route the dispatch back into the host grain's
/// idempotent-consumer surface via the deferred-dispatch callback the host
/// wired at construction. A null callback throws <see cref="NotSupportedException"/>:
/// only the <see cref="EdictIdempotencyBase{TPayload}"/> shell wires a callback,
/// and only it can stage InvokeHandler entries. Bare-named — no consumer types it.
/// </summary>
sealed class InvokeHandlerExecutor(Serializer serializer) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.InvokeHandler;

    public async Task ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task>? deferredDispatch)
    {
        var evt = serializer.Deserialize<EdictEvent>(entry.Payload);

        var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);
        using var span = EdictDiagnostics.ActivitySource.StartEdictEventHandle(
            evt.GetType().Name, parentContext);

        if (deferredDispatch is null)
        {
            throw new NotSupportedException(
                "InvokeHandler executor invoked on a host that does not wire deferred dispatch.");
        }

        await deferredDispatch(evt);
    }
}
