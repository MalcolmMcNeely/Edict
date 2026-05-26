using Edict.Contracts.Events;
using Edict.Core.Outbox;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Azure.Tests.Outbox;

/// <summary>
/// Azure-suite twin of the (deleted) Core <c>ControllableOutboxExecutor</c>:
/// a flippable <see cref="OutboxEffectKind.PublishEvent"/> executor so the
/// conformance tests can simulate a crash between the ring/outbox commit and
/// the publish, then drive a recovery drain against the <b>real Azure Queue +
/// Azure Blob</b> stack. Delegates to the real
/// <see cref="PublishEventExecutor"/> when not failing, so a successful drain
/// actually publishes to the stream.
/// </summary>
sealed class AzureControllableOutboxExecutor(Serializer serializer) : IOutboxEffectExecutor
{
    readonly PublishEventExecutor _inner = new(serializer);

    public static volatile bool ShouldFail;
    public static int FailedAttempts;

    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    public Task ExecuteAsync(
        OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent)
    {
        if (ShouldFail)
        {
            Interlocked.Increment(ref FailedAttempts);
            throw new InvalidOperationException("controllable publish failure (azure outbox test)");
        }

        return _inner.ExecuteAsync(entry, streamProvider, deferredDispatch, consumerType, liveWireEvent);
    }

    public static void Reset()
    {
        ShouldFail = false;
        FailedAttempts = 0;
    }
}
