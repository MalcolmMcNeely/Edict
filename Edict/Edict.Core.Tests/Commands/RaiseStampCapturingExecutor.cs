using System.Collections.Concurrent;

using Edict.Contracts.Events;
using Edict.Core.Outbox;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Tests.Commands;

// Records every published event so a test can read the OccurredAt stamp the
// inline drain put on the wire, without standing up a real stream subscriber.
sealed class RaiseStampCapturingExecutor(Serializer serializer) : IOutboxEffectExecutor
{
    public static readonly ConcurrentQueue<EdictEvent> Captured = new();

    public static void Reset() => Captured.Clear();

    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    public Task<OutboxEntry?> ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch,
        Type? consumerType,
        EdictEvent? liveWireEvent)
    {
        Captured.Enqueue(liveWireEvent ?? serializer.Deserialize<EdictEvent>(entry.Payload));
        return Task.FromResult<OutboxEntry?>(null);
    }
}
