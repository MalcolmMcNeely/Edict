using System.Collections.Generic;
using System.Diagnostics.Metrics;

using Edict.Contracts.Audit;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Contracts.Tenancy;
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
    readonly Func<EdictEvent, Task<OutboxEntry?>>? _deferredDispatch;
    readonly ClaimCheckPolicy? _claimCheckPolicy;
    readonly IEdictMetricsCache? _metricsCache;
    readonly Action? _requestDeactivation;
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
        Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch = null,
        ClaimCheckPolicy? claimCheckPolicy = null,
        Type? consumerType = null,
        IEdictMetricsCache? metricsCache = null,
        Action? requestDeactivation = null)
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
        _requestDeactivation = requestDeactivation;
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

    /// <summary>
    /// Atomic consumer-commit boundary. Applies the caller's in-memory progress
    /// mutation (the dedup-ring slot, optimistically advanced so a concurrent
    /// redelivery of the same in-flight event is suppressed before it can race a
    /// second write) and any staged effect, then commits
    /// <c>{ Payload, Outbox, Idempotency }</c> in one write — preserving
    /// ring-equals-row atomicity. A write fault rolls the progress mutation and
    /// the staged effect back and rethrows, so once the failure is durable a
    /// genuine redelivery is re-dispatched rather than suppressed against an id
    /// the store never persisted. The staged effect drains after a successful
    /// write, inheriting the engine's at-least-once per-entry retry.
    /// </summary>
    public async Task CommitProgressAndDrainAsync(
        Action applyProgress,
        Action rollbackProgress,
        OutboxEntry? stagedEffect,
        Action? onDrained = null)
    {
        var outboxBeforeCommit = State.Outbox;
        applyProgress();
        if (stagedEffect is { } effect)
        {
            State.Outbox = State.Outbox.Enqueue(effect with { EnqueuedAt = _timeProvider.GetUtcNow() });
        }

        try
        {
            await WriteStateAndReportAsync();
        }
        catch
        {
            rollbackProgress();
            State.Outbox = outboxBeforeCommit;
            throw;
        }

        if (stagedEffect is { } staged)
        {
            await DrainAsync();

            // Fire the post-drain hook only when the effect actually left the
            // Pending set this pass. A backed-off (transient-failed) row write
            // stays Pending, so a read-your-writes marker must not yet treat the
            // correlation as visible — the row is not in the store. The reminder
            // retry, or a reactivation drain, lands it later.
            if (onDrained is not null && State.Outbox.Pending.All(pending => pending.EntryId != staged.EntryId))
            {
                onDrained();
            }
        }
        else
        {
            // No staged effect means the consumer payload is already durable in
            // the write that just succeeded (an in-grain projection commits its
            // read model inline), so read-your-writes can be signalled now. A
            // write fault threw above before reaching here, so the hook never
            // fires ahead of durability.
            onDrained?.Invoke();
        }
    }

    /// <summary>Removes this grain's cache entry. Called by the hosting base's
    /// <c>OnDeactivateAsync</c> so a deactivated grain stops contributing to
    /// the per-type aggregate.</summary>
    public Task OnDeactivateAsync()
    {
        _metricsCache?.Remove(_grainTypeName, _grainKey);
        return Task.CompletedTask;
    }

    /// <summary>
    /// State-only commit: persists <c>{ Payload, Outbox }</c> with no enqueue and
    /// no drain. The completing-handler path takes this when no event was raised,
    /// so a consumer's <c>State</c> mutation is durable even when the Outbox stays
    /// empty.
    /// </summary>
    public Task WriteStateOnlyAsync() => WriteStateAndReportAsync();

    async Task WriteStateAndReportAsync()
    {
        try
        {
            await _state.WriteStateAsync();
        }
        catch
        {
            // The write faulted, so the activation's in-memory view — this turn's
            // consumer Payload / Progress mutation, the dedup ring, the mirror,
            // the outbox — is now diverged from the last durable snapshot.
            // Dropping the activation is the only thing that discards an arbitrary
            // consumer mutation, so a redelivery reloads clean durable state and
            // applies effectively-once rather than re-applying on top of a half-
            // applied turn.
            _requestDeactivation?.Invoke();
            throw;
        }

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
        Guid correlationId,
        EdictPrincipal? principal = null,
        EdictTenantId? tenant = null,
        Action<IReadOnlyList<EdictEvent>>? captureIdentified = null,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
        {
            return;
        }

        var policy = _claimCheckPolicy!;
        // The producer turn for any claim-check PUT the policy fires: the same
        // command context the events ride to the wire, so a spilled body's blob
        // write nests in the originating turn instead of orphaning off a null
        // Activity.Current at enqueue.
        var producerContext = ActivityExtensions.RestoreFromTraceParent(traceParent, traceState);
        // Assign EventId once, as the event enters the Outbox, before the policy
        // serialises the payload — so the persisted bytes carry the stable
        // identity and a re-drain deserialises the same id. OccurredAt was
        // already stamped at Raise; this is the delivery identity, not the
        // intent stamp. The correlation id, principal, and tenant are stamped here
        // too: the event inherits all from the message that caused it, carried
        // unchanged so the whole chain shares one chain-stable token, stays
        // attributed to the same actor, and stays inside the same tenant wall.
        var identified = new EdictEvent[events.Count];
        for (var i = 0; i < events.Count; i++)
        {
            identified[i] = events[i] with { EventId = Guid.NewGuid(), ConversationId = correlationId, Principal = principal, Tenant = tenant };
        }

        // E1 audit capture rides the same write that commits the enqueue: the
        // identified events are handed back to the grain to stage one record each
        // onto the audit chain before the state below is written, so the chain
        // never diverges from the events it attests to.
        captureIdentified?.Invoke(identified);

        var results = await Task.WhenAll(identified.Select(edictEvent =>
            policy.ApplyAsync(edictEvent, producerContext, cancellationToken)));

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
        // Parallel drain is sound because executors are independent and
        // consumers are already reorder-tolerant (the dedup ring is keyed
        // by EventId, not delivery order). The original v1 rationale
        // assumed WriteStateAsync dominated; the throughput bench showed
        // the queue PUT inside the executor dominates for RaiseOnly, so
        // the lever sits here.
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
            var keyByEntry = new Dictionary<Guid, (string, string)>(ready.Length);
            for (var i = 0; i < ready.Length; i++)
            {
                var entry = ready[i];
                EdictEvent? live = null;
                liveRefs?.TryGetValue(entry.EntryId, out live);
                var executor = _executors[entry.Kind];
                var resolved = executor.TryResolveBatchKey(entry, live);
                keyByEntry[entry.EntryId] = resolved is { } r
                    ? (r.StreamName, r.RouteKey)
                    : (string.Empty, entry.EntryId.ToString("N"));

                // Forward only the genuine in-memory reference — null on a recovery
                // drain. The publish executor branches its span topology on this:
                // a live ref nests under the staging command (same turn), a null
                // ref makes the publish its own root linking back (later turn).
                liveByEntry[entry.EntryId] = live;
            }

            var groups = OutboxBatchGrouping.Group(ready, e => keyByEntry[e.EntryId]);

            var groupTasks = new Task<GroupOutcome>[groups.Count];
            for (var i = 0; i < groups.Count; i++)
            {
                groupTasks[i] = ExecuteGroupCapturingAsync(groups[i].Entries, liveByEntry);
            }

            var outcomes = await Task.WhenAll(groupTasks);

            var anyTailAppend = false;
            var anyStagedEffect = false;
            for (var g = 0; g < groups.Count; g++)
            {
                var groupEntries = groups[g].Entries;
                var exception = outcomes[g].Failure;

                if (exception is null)
                {
                    foreach (var entry in groupEntries)
                    {
                        State.Outbox = State.Outbox.Ack(entry.EntryId);
                        dirty = true;
                    }

                    // A deferred saga / table-projection dispatch returns its
                    // downstream effect; enqueue it so the ack of the
                    // InvokeHandler entry and the staged effect commit in one
                    // write (flushed below before the effect drains). The
                    // InvokeHandler entry is the durability anchor — a crash
                    // before that write re-runs the handler and regenerates the
                    // effect; a crash after it leaves only the effect Pending,
                    // so the effect drains exactly once.
                    foreach (var staged in outcomes[g].StagedEffects)
                    {
                        State.Outbox = State.Outbox.Enqueue(staged with { EnqueuedAt = now });
                        dirty = true;
                        anyStagedEffect = true;
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

            // Flush the composed ack-write (InvokeHandler acks + their staged
            // effects) to durable storage before looping to drain those
            // effects, so the ack is the durability boundary: a crash here
            // re-runs the handler; a crash while the effect drains next pass
            // leaves only the effect Pending. Without this the effect would
            // execute before its own enqueue is durable.
            if (anyStagedEffect && dirty)
            {
                await WriteStateAndReportAsync();
                dirty = false;
            }

            // Loop if a Promote appended a tail entry that's ready now, or a
            // deferred dispatch staged a downstream effect this pass — both are
            // freshly-Pending and ready, so drain them in the same call rather
            // than waiting for a reminder. Otherwise we're done; any
            // backoff-gated entries wait for the reminder.
            if (!anyTailAppend && !anyStagedEffect)
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

    readonly record struct GroupOutcome(Exception? Failure, IReadOnlyList<OutboxEntry> StagedEffects)
    {
        public static readonly GroupOutcome NoEffects = new(null, []);
        public static GroupOutcome Failed(Exception exception) => new(exception, []);
    }

    async Task<GroupOutcome> ExecuteGroupCapturingAsync(
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

            var staged = await _executors[entries[0].Kind].ExecuteBatchAsync(
                entries, _streamProvider, _deferredDispatch, _consumerType, liveBatch);
            return staged.Count == 0 ? GroupOutcome.NoEffects : new GroupOutcome(null, staged);
        }
        catch (Exception exception)
        {
            return GroupOutcome.Failed(exception);
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
