using System.Collections.Generic;
using System.Diagnostics.Metrics;

using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.DeadLetter;
using Edict.Core.Metrics;
using Edict.Telemetry;

using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Outbox;

static class OutboxDrainMetrics
{
    // Held in a non-generic class so the static initializer runs once per process
    // instead of once per closed generic of OutboxHost<TPayload>.
    public static readonly Counter<long> DrainCount = EdictDiagnostics.Meter.CreateCounter<long>(
        SemanticConventions.Outbox.Meters.DrainCount);
    public static readonly Histogram<int> DrainEntries = EdictDiagnostics.Meter.CreateHistogram<int>(
        SemanticConventions.Outbox.Meters.DrainEntries);
}

sealed class OutboxHost<TPayload>
    where TPayload : new()
{
    internal const string DrainReminderName = "edict-outbox-drain";

    readonly IPersistentState<GrainEnvelope<TPayload>> _state;
    readonly IStreamProvider _streamProvider;
    readonly IReminderRegistrar _reminders;
    readonly IReadOnlyDictionary<OutboxEffectKind, IOutboxEffectExecutor> _executors;
    readonly EdictOptions _options;
    readonly TimeProvider _timeProvider;
    readonly IDeadLetterPromoter _promoter;
    readonly Func<EdictEvent, Task>? _deferredDispatch;
    readonly ClaimCheckPolicy? _claimCheckPolicy;
    readonly IEdictMetricsCache? _metricsCache;
    readonly string _grainKey;
    readonly string _grainTypeName;
    readonly Type? _consumerType;

    bool _drainReminderRegistered;

    // Set by EnqueueAndDrainAsync for the inline drain that immediately
    // follows; cleared inside DrainAsync. Activation / reminder drains leave
    // it null so the executor falls back to deserialising the durable payload.
    Dictionary<Guid, EdictEvent>? _inlineLiveRefs;

    public OutboxHost(
        IPersistentState<GrainEnvelope<TPayload>> state,
        IStreamProvider streamProvider,
        IReminderRegistrar reminders,
        IEnumerable<IOutboxEffectExecutor> executors,
        EdictOptions options,
        TimeProvider timeProvider,
        IDeadLetterPromoter promoter,
        string grainKey,
        string grainTypeName,
        Func<EdictEvent, Task>? deferredDispatch = null,
        ClaimCheckPolicy? claimCheckPolicy = null,
        Type? consumerType = null,
        IEdictMetricsCache? metricsCache = null)
    {
        _state = state;
        _streamProvider = streamProvider;
        _reminders = reminders;
        _executors = executors.ToDictionary(static e => e.Kind);
        _options = options;
        _timeProvider = timeProvider;
        _promoter = promoter;
        _grainKey = grainKey;
        _grainTypeName = grainTypeName;
        _deferredDispatch = deferredDispatch;
        _claimCheckPolicy = claimCheckPolicy;
        _consumerType = consumerType;
        _metricsCache = metricsCache;
    }

    /// <summary>The persisted envelope <c>{ Payload, Outbox, Idempotency }</c>.</summary>
    public GrainEnvelope<TPayload> State => _state.State;

    /// <summary>Drain-on-activation: catches anything left from a crash before the grain serves traffic.</summary>
    public async Task OnActivateAsync()
    {
        if (State.Outbox.Pending.Count > 0)
        {
            await DrainAsync();
        }
    }

    /// <summary>
    /// Reminder tick — the lazy crash-recovery retry path. A tick proves a
    /// reminder exists, so the post-drain reconcile authoritatively
    /// unregisters it once the Outbox is empty.
    /// </summary>
    public Task ReceiveReminderAsync()
    {
        _drainReminderRegistered = true;
        return DrainAsync();
    }

    /// <summary>
    /// Stages the supplied entries onto the Outbox, commits
    /// <c>{ Payload, Outbox, Idempotency }</c> in one write, then awaits the
    /// inline drain. The commit is the durability point — <c>Send()</c>
    /// returns <c>Accepted</c> once it (and the awaited drain) completes.
    /// </summary>
    public async Task EnqueueAndDrainAsync(IReadOnlyList<OutboxEntry> entries)
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var entry in entries)
        {
            // Stamp EnqueuedAt at the single enqueue choke point so every entry
            // carries the host clock's view of when it joined Pending. Callers
            // that construct entries (EdictSaga, the pointer-envelope branch in
            // EdictIdempotencyBase, EnqueueRaisedEventsAndDrainAsync) don't
            // need to know about the field.
            State.Outbox = State.Outbox.Enqueue(entry with { EnqueuedAt = now });
        }

        await WriteStateAndReportAsync();
        await DrainAsync();
    }

    /// <summary>Removes this grain's cache entry. Called by the hosting base's
    /// <c>OnDeactivateAsync</c> so a deactivated grain stops contributing to
    /// the per-type aggregate (ADR-0040's load-bearing cleanup).</summary>
    public Task OnDeactivateAsync()
    {
        _metricsCache?.Remove(_grainTypeName, _grainKey);
        return Task.CompletedTask;
    }

    async Task WriteStateAndReportAsync()
    {
        await _state.WriteStateAsync();
        ReportPendingToCache();
    }

    void ReportPendingToCache()
    {
        if (_metricsCache is null)
        {
            return;
        }

        var pending = State.Outbox.Pending;
        DateTimeOffset? oldest = null;
        foreach (var entry in pending)
        {
            if (oldest is null || entry.EnqueuedAt < oldest)
            {
                oldest = entry.EnqueuedAt;
            }
        }
        _metricsCache.ReportOutbox(_grainTypeName, _grainKey, pending.Count, oldest);
    }

    /// <summary>
    /// Event-aware commit boundary. Routes every buffered
    /// event through <see cref="ClaimCheckPolicy"/> in parallel via
    /// <see cref="Task.WhenAll(IEnumerable{Task})"/>, so a Handle that raises
    /// N oversized events pays one I/O round trip rather than N. Each policy
    /// invocation returns the bytes to persist as the
    /// <see cref="OutboxEntry.Payload"/> paired with the live wire-event the
    /// inline drain publishes without re-deserialising. Small events ride the
    /// entry as the serialised inner event; oversized events as a serialised
    /// pointer envelope. The staged entries then commit and drain through the
    /// engine.
    /// </summary>
    public async Task EnqueueRaisedEventsAndDrainAsync(
        IReadOnlyList<EdictEvent> events,
        string? traceParent,
        string? traceState,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
        {
            return;
        }

        if (_claimCheckPolicy is null)
        {
            throw new InvalidOperationException(
                "EnqueueRaisedEventsAndDrainAsync requires a ClaimCheckPolicy; none was registered on this host.");
        }

        var policy = _claimCheckPolicy;
        var results = await Task.WhenAll(events.Select(edictEvent => policy.ApplyAsync(edictEvent, cancellationToken)));

        var entries = new OutboxEntry[events.Count];
        var liveRefs = new Dictionary<Guid, EdictEvent>(events.Count);
        for (var i = 0; i < events.Count; i++)
        {
            var entryId = Guid.NewGuid();
            entries[i] = new OutboxEntry
            {
                EntryId = entryId,
                Kind = OutboxEffectKind.PublishEvent,
                Payload = results[i].Payload,
                TraceParent = traceParent,
                TraceState = traceState,
            };
            liveRefs[entryId] = results[i].WireEvent;
        }

        _inlineLiveRefs = liveRefs;
        await EnqueueAndDrainAsync(entries);
    }

    /// <summary>
    /// Drains pending effects with per-entry independent retry. Each pass
    /// snapshots every entry whose <see cref="OutboxEntry.NextAttemptUtc"/>
    /// is now-or-past and fires their executors concurrently via
    /// <see cref="Task.WhenAll(IEnumerable{Task})"/>; outcomes (Ack / Fail /
    /// Promote) are applied to the slice serially on the grain task scheduler
    /// after the batch completes, so the slice stays a pure data structure
    /// with no cross-task contention. Successful Acks coalesce into one
    /// trailing write per pass. Failure paths (FailWithBackoff / Promote)
    /// keep their inline writes for <c>AttemptCount</c> crash-monotonicity.
    /// At <see cref="EdictOptions.OutboxMaxAttempts"/> the failing entry is
    /// promoted to a dead-letter publish entry appended at the tail; the
    /// outer loop picks that tail entry up in the next pass. Reconciles the
    /// lazy Reminder: unregistered when the Outbox fully drains, registered
    /// while anything remains.
    /// </summary>
    public async Task DrainAsync()
    {
        // At-least-once delivery permits batching ack-durability across a pass:
        // a mid-pass crash re-executes already-shipped entries, which the
        // consumer dedup ring suppresses. The trailing write before the
        // reminder reconcile is the load-bearing ordering — a reminder must
        // never observe a "drained" state that has not yet been persisted, or
        // a crash between the unregister and a missing write would lose the
        // pending tail.
        //
        // Parallel drain is sound under ADR-0015 because executors are
        // independent and consumers are already reorder-tolerant (the dedup
        // ring is keyed by EventId, not delivery order). The original v1
        // rationale assumed WriteStateAsync dominated; the throughput bench
        // showed the queue PUT inside the executor dominates for RaiseOnly,
        // so the lever sits here.
        var liveRefs = _inlineLiveRefs;
        _inlineLiveRefs = null;

        var dirty = false;

        while (true)
        {
            var now = _timeProvider.GetUtcNow();
            var ready = State.Outbox.Pending
                .Where(p => p.NextAttemptUtc <= now)
                .ToArray();

            if (ready.Length == 0)
            {
                break;
            }

            var grainTypeTag = new KeyValuePair<string, object?>(
                SemanticConventions.Common.Tags.GrainType, _grainTypeName);
            OutboxDrainMetrics.DrainCount.Add(1, grainTypeTag);
            OutboxDrainMetrics.DrainEntries.Record(ready.Length, grainTypeTag);

            // Per-entry batch-key resolution. Entries that opt out (the
            // default for every non-PublishEvent executor) get a synthetic
            // entry-id-scoped key so the grouping function leaves them in
            // singleton groups. PublishEvent entries return their stream
            // address, which lets the grouping function coalesce a run of
            // events headed for the same (streamName, routeKey) into a
            // single OnNextBatchAsync.
            var liveByEntry = new Dictionary<Guid, EdictEvent?>(ready.Length);
            var keyByEntry = new Dictionary<Guid, (string, Guid)>(ready.Length);
            for (var i = 0; i < ready.Length; i++)
            {
                var entry = ready[i];
                EdictEvent? live = null;
                liveRefs?.TryGetValue(entry.EntryId, out live);
                var executor = _executors[entry.Kind];
                var resolved = executor.TryResolveBatchKey(entry, live);
                if (resolved is { } r)
                {
                    keyByEntry[entry.EntryId] = (r.StreamName, r.RouteKey);
                    liveByEntry[entry.EntryId] = r.ResolvedEvent ?? live;
                }
                else
                {
                    // Synthetic key — entry.EntryId is unique so a non-batchable
                    // entry never coalesces with anything.
                    keyByEntry[entry.EntryId] = (string.Empty, entry.EntryId);
                    liveByEntry[entry.EntryId] = live;
                }
            }

            var groups = OutboxBatchGrouping.Group(ready, e => keyByEntry[e.EntryId]);

            var groupTasks = new Task<Exception?>[groups.Count];
            for (var i = 0; i < groups.Count; i++)
            {
                groupTasks[i] = ExecuteGroupCapturingAsync(groups[i].Entries, liveByEntry);
            }

            var outcomes = await Task.WhenAll(groupTasks);

            var anyTailAppend = false;
            for (var g = 0; g < groups.Count; g++)
            {
                var groupEntries = groups[g].Entries;
                var exception = outcomes[g];

                if (exception is null)
                {
                    foreach (var entry in groupEntries)
                    {
                        State.Outbox = State.Outbox.Ack(entry.EntryId);
                        dirty = true;
                    }
                    continue;
                }

                // Batch failure unwinds to single-entry FailWithBackoff /
                // Promote for every entry that was in the failing group
                // (OnNextBatchAsync is all-or-nothing per the substrate
                // contract; partial-success accounting is out of scope).
                foreach (var entry in groupEntries)
                {
                    State.Outbox = State.Outbox.FailWithBackoff(entry.EntryId, now, _options);

                    var bumped = State.Outbox.Pending.FirstOrDefault(p => p.EntryId == entry.EntryId);
                    if (bumped is not null && bumped.AttemptCount >= _options.OutboxMaxAttempts)
                    {
                        var promoted = _promoter.Promote(
                            bumped, exception, _grainKey, _grainTypeName, now)
                            with { EnqueuedAt = now };
                        State.Outbox = State.Outbox.Promote(entry.EntryId, promoted);
                        await WriteStateAndReportAsync();
                        dirty = false;
                        anyTailAppend = true;
                        continue;
                    }

                    await WriteStateAndReportAsync();
                    dirty = false;
                }
            }

            // Loop only if a Promote appended a tail entry that's ready now.
            // Otherwise we're done — any backoff-gated entries wait for the
            // reminder.
            if (!anyTailAppend)
            {
                break;
            }
        }

        if (dirty)
        {
            await WriteStateAndReportAsync();
        }
        else
        {
            // No coalesced trailing write — every state mutation was already
            // persisted inline (FailWithBackoff / Promote) or the drain found
            // nothing ready. Push the current Pending view anyway so an
            // activation drain that opened with gated entries seeds the cache
            // with their depth + earliest enqueue time even though no write
            // ran this pass.
            ReportPendingToCache();
        }

        if (State.Outbox.Pending.Count == 0)
        {
            await UnregisterDrainReminderAsync();
        }
        else
        {
            await RegisterDrainReminderAsync();
        }
    }

    async Task<Exception?> ExecuteGroupCapturingAsync(
        IReadOnlyList<OutboxEntry> entries,
        IReadOnlyDictionary<Guid, EdictEvent?> liveByEntry)
    {
        try
        {
            var liveBatch = new EdictEvent?[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                liveBatch[i] = liveByEntry[entries[i].EntryId];
            }

            await _executors[entries[0].Kind].ExecuteBatchAsync(
                entries, _streamProvider, _deferredDispatch, _consumerType, liveBatch);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    async Task RegisterDrainReminderAsync()
    {
        await _reminders.RegisterOrUpdateReminderAsync(
            DrainReminderName, _options.OutboxDrainReminderPeriod, _options.OutboxDrainReminderPeriod);
        _drainReminderRegistered = true;
    }

    async Task UnregisterDrainReminderAsync()
    {
        if (!_drainReminderRegistered)
        {
            return; // never registered — keep the happy path off the reminder subsystem
        }

        await _reminders.UnregisterReminderAsync(DrainReminderName);
        _drainReminderRegistered = false;
    }
}
