using Edict.Contracts.Events;
using Edict.Core.Outbox;
using Edict.Telemetry;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Tests.Conformance.Outbox;

/// <summary>
/// Flippable <see cref="OutboxEffectKind.PublishEvent"/> executor used by the
/// outbox conformance scenarios to simulate a crash between the ring/outbox
/// commit and the publish, then drive a recovery drain against the bound
/// substrate. Delegates to the real <see cref="PublishEventExecutor"/> when
/// not failing, so a successful drain actually publishes to the stream.
/// The static <see cref="ShouldFail"/> flag is process-wide; every concrete
/// fixture that wires this executor must serialise its tests via an xUnit
/// collection so the toggle does not race across fixture shapes.
/// </summary>
public sealed class ControllableOutboxExecutor(Serializer serializer, IEventStreamAccessors accessors, IEventTagWriters tagWriters) : IOutboxEffectExecutor
{
    readonly PublishEventExecutor _inner = new(serializer, accessors, tagWriters);

    public static volatile bool ShouldFail;
    public static int FailedAttempts;

    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    public Task ExecuteAsync(
        OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent)
    {
        if (ShouldFail)
        {
            Interlocked.Increment(ref FailedAttempts);
            throw new InvalidOperationException("controllable publish failure (outbox conformance test)");
        }

        return _inner.ExecuteAsync(entry, streamProvider, deferredDispatch, consumerType, liveWireEvent);
    }

    public static void Reset()
    {
        ShouldFail = false;
        FailedAttempts = 0;
    }
}
