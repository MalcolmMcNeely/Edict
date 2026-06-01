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
    /// <para>
    /// Returns a downstream effect the execution staged that must be enqueued in
    /// the same grain-state write that acks this entry, or <c>null</c> for the
    /// common case. Only <c>InvokeHandler</c> returns one: a deferred saga /
    /// table-projection dispatch stages its <c>SendCommand</c> / <c>UpsertRow</c>.
    /// </para>
    /// </summary>
    Task<OutboxEntry?> ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch,
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
    /// Returns the downstream effects staged across the group (empty for every
    /// kind but <c>InvokeHandler</c>) for the host to enqueue atomically with
    /// the group's ack.
    /// </summary>
    async Task<IReadOnlyList<OutboxEntry>> ExecuteBatchAsync(
        IReadOnlyList<OutboxEntry> entries,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch,
        Type? consumerType,
        IReadOnlyList<EdictEvent?> liveWireEvents)
    {
        var tasks = new Task<OutboxEntry?>[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            tasks[i] = ExecuteAsync(
                entries[i], streamProvider, deferredDispatch, consumerType, liveWireEvents[i]);
        }

        var staged = await Task.WhenAll(tasks);
        var effects = new List<OutboxEntry>();
        foreach (var effect in staged)
        {
            if (effect is not null)
            {
                effects.Add(effect);
            }
        }
        return effects;
    }
}
