using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.Outbox;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Tests.DeadLetter;

// Fails every PublishEvent except EdictDeadLetterRaised — drives the engine's
// promotion path while letting the dead-letter notification itself publish
// cleanly so the projection grain can write the row.
sealed class DeadLetterAwarePublishExecutor(Serializer serializer, IEventStreamAccessors accessors) : IOutboxEffectExecutor
{
    readonly PublishEventExecutor _inner = new(serializer, accessors);

    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    public Task ExecuteAsync(
        OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent)
    {
        var evt = liveWireEvent ?? serializer.Deserialize<EdictEvent>(entry.Payload);
        if (evt is EdictDeadLetterRaised)
        {
            return _inner.ExecuteAsync(entry, streamProvider, deferredDispatch, consumerType, liveWireEvent);
        }
        throw new InvalidOperationException("simulated permanent publish failure");
    }
}
