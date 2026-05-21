using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.Outbox;
using Edict.Telemetry;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Testing.Internal;

/// <summary>
/// Replaces the bare <see cref="OutboxEffectKind.InvokeHandler"/> executor in
/// the shipped Test Framework. Mirrors the production executor — deserialise
/// the buffered <see cref="EdictEventEnvelope"/>, materialise the inner event
/// via <see cref="ClaimCheckUnwrap"/>, restore the captured
/// <c>traceparent</c>, open the deferred-invocation span, and route the
/// dispatch back through the host's deferred-dispatch callback — but also
/// records an <c>Invocation</c> timeline entry with the <c>Ran</c> outcome
/// once the consumer's <c>Handle</c> returns. Permanent-failure outcomes
/// (dead-letter promotion) are recorded out-of-band by the publish executor
/// when it observes the framework's <c>EdictDeadLetterRaised</c> event with
/// <c>Kind = InvokeHandler</c>, because the host's promotion path bypasses
/// this executor on the final attempt.
/// </summary>
sealed class InProcInvokeHandlerExecutor(
    Serializer serializer,
    ClaimCheckUnwrap unwrap,
    TimelineRecorder recorder) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.InvokeHandler;

    public async Task ExecuteAsync(
        OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch, Type? consumerType)
    {
        if (deferredDispatch is null)
        {
            throw new NotSupportedException(
                "InProcInvokeHandlerExecutor invoked on a host that does not wire deferred dispatch.");
        }

        var staged = serializer.Deserialize<EdictEvent>(entry.Payload);
        var materialised = await unwrap.ApplyAsync(
            staged, consumerType ?? typeof(object), CancellationToken.None);

        var parentContext = ActivityExtensions.RestoreFromTraceParent(entry.TraceParent, entry.TraceState);
        using var span = EdictDiagnostics.ActivitySource.StartEdictEventHandle(
            materialised.GetType().Name, parentContext);

        await deferredDispatch(materialised);

        recorder.RecordInvocation(materialised.GetType().Name, materialised.EventId, "Ran");
    }
}
