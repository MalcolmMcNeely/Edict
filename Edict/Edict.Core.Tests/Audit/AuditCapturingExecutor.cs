using System.Collections.Concurrent;

using Edict.Contracts.Events;
using Edict.Core.Outbox;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Tests.Audit;

// Records every published event off the outbox seam so a test can read the
// principal the inline drain stamped onto the wire, without a real subscriber.
// Its own static (distinct from the RaiseStamp executor's) keeps the audit
// collection isolated from collections that run in parallel.
sealed class AuditCapturingExecutor(Serializer serializer) : IOutboxEffectExecutor
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
