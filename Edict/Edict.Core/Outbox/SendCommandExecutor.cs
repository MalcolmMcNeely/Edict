using Edict.Contracts.Commands;
using Edict.Contracts.Sending;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Core.Outbox;

/// <summary>
/// Drains a <see cref="OutboxEffectKind.SendCommand"/> entry: deserialize the
/// buffered command and route it through <see cref="IEdictSender"/> (ADR 0020).
/// The entry's captured <c>traceparent</c> (the saga's handle span) is restored
/// as the parent of an <c>edict.command.send</c> span, under which
/// <see cref="IEdictSender.Send"/> opens its own command span — so the saga's
/// Event→Command hop stays parent-child and survives a crash-recovery drain
/// (ADR 0003). The sender is resolved lazily so a host that wires the Outbox
/// but registers no command routes still constructs the engine (mirrors
/// <see cref="UpsertRowExecutor"/>). Bare-named — no consumer types it.
/// </summary>
sealed class SendCommandExecutor(Serializer serializer, IServiceProvider services) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.SendCommand;

    public async Task ExecuteAsync(OutboxEntry entry, IOutboxHost host)
    {
        var command = serializer.Deserialize<EdictCommand>(entry.Payload);

        var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);
        using var activity = EdictDiagnostics.ActivitySource.StartEdictCommandSend(
            command.GetType().Name, parentContext);

        var sender = services.GetRequiredService<IEdictSender>();
        await sender.Send(command);
    }
}
