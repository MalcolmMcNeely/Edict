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

    /// <summary>
    /// Returns the stream address used to coalesce consecutive ready entries
    /// into one batched dispatch. The optional <c>ResolvedEvent</c> lets the
    /// host avoid a second deserialise by forwarding it as the live ref into
    /// <see cref="ExecuteBatchAsync"/>. Default <c>null</c> opts the entry
    /// out of batching — the host treats it as a singleton group.
    /// </summary>
    (string StreamName, Guid RouteKey, EdictEvent? ResolvedEvent)? TryResolveBatchKey(
        OutboxEntry entry, EdictEvent? liveWireEvent) => null;

    /// <summary>
    /// Executes a contiguous group of entries that share a batch key. The
    /// default fans out to <see cref="ExecuteAsync"/> per entry so executors
    /// that have no native batch path (UpsertRow, SendCommand,
    /// InvokeHandler) keep their existing semantics. PublishEvent overrides
    /// this to call <c>IAsyncStream.OnNextBatchAsync</c> once per group.
    /// </summary>
    Task ExecuteBatchAsync(
        IReadOnlyList<OutboxEntry> entries,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task>? deferredDispatch,
        Type? consumerType,
        IReadOnlyList<EdictEvent?> liveWireEvents)
    {
        var tasks = new Task[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            tasks[i] = ExecuteAsync(
                entries[i], streamProvider, deferredDispatch, consumerType, liveWireEvents[i]);
        }
        return Task.WhenAll(tasks);
    }
}
