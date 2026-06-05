using System.Collections.Concurrent;

using Edict.Contracts.Events;
using Edict.Core.Outbox;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Tests.Schedules;

// Records every event a schedule fire publishes so a test can read the
// correlation the inline drain stamped on the wire, without standing up a real
// stream subscriber. Own static queue so it never crosses the (parallel) command
// or saga capturing executors.
sealed class ScheduleRaiseCapturingExecutor(Serializer serializer) : IOutboxEffectExecutor
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
