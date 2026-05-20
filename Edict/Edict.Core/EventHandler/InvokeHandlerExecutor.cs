using Edict.Contracts.Events;
using Edict.Core.Outbox;
using Edict.Telemetry;

using Orleans.Serialization;

namespace Edict.Core.EventHandler;

/// <summary>
/// Drains a <see cref="OutboxEffectKind.InvokeHandler"/> entry (ADR 0023):
/// deserialise the buffered <see cref="EdictEvent"/>, restore the captured
/// <c>traceparent</c> so the deferred invocation span nests under the publish
/// span as parent-child even when backoff defers the call across the stream
/// hop (ADR 0003), and route the dispatch back into the host grain's
/// idempotent-consumer surface via <see cref="IOutboxHost.DispatchEventAsync"/>.
/// The executor has no Orleans dependency in its logic body — it is trivially
/// unit-testable against a fake <see cref="IOutboxHost"/>. Bare-named — no
/// consumer types it.
/// </summary>
sealed class InvokeHandlerExecutor(Serializer serializer) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.InvokeHandler;

    public async Task ExecuteAsync(OutboxEntry entry, IOutboxHost host)
    {
        var evt = serializer.Deserialize<EdictEvent>(entry.Payload);

        var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);
        using var span = EdictDiagnostics.ActivitySource.StartEdictEventHandle(
            evt.GetType().Name, parentContext);

        await host.DispatchEventAsync(evt);
    }
}
