using Edict.Contracts.Events;

using Orleans.Streams;

namespace Edict.Core.Outbox;

interface IOutboxEffectExecutor
{
    OutboxEffectKind Kind { get; }

    /// <summary>
    /// Executes one outbox entry. The host may pass <paramref name="liveWireEvent"/>
    /// when the entry was just enqueued and the live event reference is still
    /// in hand — the PublishEvent executor uses it to skip a serialise→deserialise
    /// round trip. Crash-recovery drains (activation, reminder) pass <c>null</c>
    /// and the executor falls back to deserialising <see cref="OutboxEntry.Payload"/>.
    /// </summary>
    Task ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task>? deferredDispatch,
        Type? consumerType,
        EdictEvent? liveWireEvent);
}
