using System.Diagnostics.CodeAnalysis;

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

    // The deferred path deserialises an EdictCommand from persisted state,
    // so the call site is unavoidably base-typed. EDICT015 exists to catch
    // the same shape in *consumer* code, where the typed receiver is
    // statically knowable. Edict.Core does not reference the analyzer, so
    // this attribute is documentary today and a future-proof guard.
    [SuppressMessage(
        "Edict", "EDICT015",
        Justification = "Framework deferred dispatch from persisted state — base-typed by design.")]
    public async Task<OutboxEntry?> ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch,
        Type? consumerType,
        EdictEvent? liveWireEvent)
    {
        var command = serializer.Deserialize<EdictCommand>(entry.Payload);

        var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);
        using var activity = EdictDiagnostics.ActivitySource.StartEdictCommandSend(
            command.GetType().Name, parentContext);

        // A saga's dispatch is fire-and-forget: the handled command runs in its own
        // grain turn. Carry this send span as the cross-turn link source so the
        // receiving edict.command.handle links back to it as a new root, and the
        // intervening edict.command does not clobber it as the carrier. The marker
        // also exempts this relayed send from origin fail-closed in the receiving
        // identity stampers, so it must be set even when no span is recording —
        // otherwise an untraced audited dispatch of a principal-less command would
        // wrongly fail closed and dead-letter.
        if (activity is not null)
        {
            activity.CaptureToRequestContext(crossTurnLink: true);
        }
        else
        {
            ActivityExtensions.MarkCrossTurnLink();
        }

        var sender = services.GetRequiredService<IEdictSender>();
        await sender.SendAsync(command);
        return null;
    }
}
