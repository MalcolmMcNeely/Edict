using Edict.Contracts.Events;
using Edict.Core.Outbox;
using Edict.Telemetry;
using Edict.Testing.Recording;

using Orleans.Serialization;

namespace Edict.Testing.InProcess;

/// <summary>
/// Replaces the bare <see cref="OutboxEffectKind.InvokeHandler"/> executor in
/// the shipped Test Framework (ADR 0023). Mirrors the production executor's
/// behaviour — deserialise the buffered <see cref="EdictEvent"/>, restore the
/// captured <c>traceparent</c>, open the deferred-invocation span, and route
/// the dispatch back through <see cref="IOutboxHost.DispatchEventAsync"/> — but
/// also records an <c>Invocation</c> timeline entry with the <c>Ran</c> outcome
/// once the consumer's <c>Handle</c> returns. Permanent-failure outcomes
/// (dead-letter promotion) are recorded out-of-band by the
/// <c>InProcPublishEventExecutor</c> when it observes the framework's
/// <c>EdictDeadLetterRaised</c> event with <c>Kind = InvokeHandler</c>, because
/// the engine's promotion path bypasses this executor on the final attempt.
/// Bare-named — no consumer types it.
/// </summary>
sealed class InProcInvokeHandlerExecutor(
    Serializer serializer,
    EdictTimelineRecorder recorder) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.InvokeHandler;

    public async Task ExecuteAsync(OutboxEntry entry, IOutboxHost host)
    {
        var evt = serializer.Deserialize<EdictEvent>(entry.Payload);

        var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);
        using var span = EdictDiagnostics.ActivitySource.StartEdictEventHandle(
            evt.GetType().Name, parentContext);

        await host.DispatchEventAsync(evt);

        recorder.RecordInvocation(evt.GetType().Name, evt.EventId, "Ran");
    }
}
