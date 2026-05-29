namespace Edict.Core.Metrics;

/// <summary>
/// Silo-local cache fed by every <c>OutboxHost</c> commit and <c>EdictSaga</c>
/// event-handle, scraped by the three observable gauges
/// (<c>edict.outbox.pending.count</c>, <c>edict.outbox.oldest_entry.age</c>,
/// <c>edict.saga.progress.age</c>). Replaces a per-scrape grain fan-out
/// (ADR-0040): the push happens on the grain task scheduler as part of work
/// the host is already doing, the gauge callback reads a
/// <c>ConcurrentDictionary</c> bounded by the number of currently-active grains
/// on this silo. The cache instance is registered as a silo singleton by
/// <c>AddEdict()</c>; <c>Edict.Testing</c> swaps it for an assertable variant
/// behind the same interface.
/// </summary>
public interface IEdictMetricsCache
{
    /// <summary>
    /// Records the outbox depth and the oldest pending entry's enqueue time for
    /// one host. Called by <c>OutboxHost</c> after every successful state
    /// mutation (the cache is read-after-write consistent with the host's view
    /// of <c>Pending</c>). Pass <c>pendingCount</c> = 0 and
    /// <c>oldestEnqueuedAt</c> = null when the host has fully drained; the
    /// gauge callback ignores zero-depth entries so a drained host contributes
    /// nothing to the per-type aggregate. Synchronous: a single
    /// <c>ConcurrentDictionary</c> mutation, no <c>Task</c>, no <c>Task.Run</c>.
    /// </summary>
    void ReportOutbox(string grainType, string grainKey, int pendingCount, DateTimeOffset? oldestEnqueuedAt);

    /// <summary>
    /// Records the timestamp at which a saga last handled an event. Called by
    /// <c>EdictSaga.DispatchEventAsync</c> after the consumer's handler
    /// completes. Synchronous: a single <c>ConcurrentDictionary</c> mutation.
    /// </summary>
    void ReportSaga(string sagaType, string sagaKey, DateTimeOffset lastHandledAt);

    /// <summary>
    /// Removes every cached entry for the supplied <c>(grainType, grainKey)</c>
    /// — both outbox and saga state. Called from the hosting grain's
    /// <c>OnDeactivateAsync</c> so a deactivated grain stops contributing to
    /// the per-type aggregate; without this hook a grain that deactivates with
    /// <c>pendingCount = 5</c> would continue to contribute 5 forever
    /// (ADR-0040's load-bearing cleanup).
    /// </summary>
    void Remove(string grainType, string grainKey);
}
