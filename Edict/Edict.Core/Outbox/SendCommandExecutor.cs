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

    public async Task ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task>? deferredDispatch,
        Type? consumerType)
    {
        var command = serializer.Deserialize<EdictCommand>(entry.Payload);

        var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);
        using var activity = EdictDiagnostics.ActivitySource.StartEdictCommandSend(
            command.GetType().Name, parentContext);

        var sender = services.GetRequiredService<IEdictSender>();
        await sender.Send(command);
    }
}
