using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Sending;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Outbox;

sealed class SendCommandExecutor(Serializer serializer, IServiceProvider services) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.SendCommand;

    // The deferred path deserialises an EdictCommand from persisted state, so
    // the call site is unavoidably base-typed. EDICT015 (ADR-0034) exists to
    // catch the same shape in *consumer* code, where the typed receiver is
    // statically knowable. Edict.Core does not reference the analyzer, so
    // this attribute is documentary today and a future-proof guard.
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Edict", "EDICT015",
        Justification = "Framework deferred dispatch from persisted state — base-typed by design (ADR-0034).")]
    public async Task ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task>? deferredDispatch,
        Type? consumerType,
        EdictEvent? liveWireEvent)
    {
        var command = serializer.Deserialize<EdictCommand>(entry.Payload);

        var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);
        using var activity = EdictDiagnostics.ActivitySource.StartEdictCommandSend(
            command.GetType().Name, parentContext);

        var sender = services.GetRequiredService<IEdictSender>();
        await sender.Send(command);
    }
}
