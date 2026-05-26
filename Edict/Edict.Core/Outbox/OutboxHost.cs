using System.Collections.Generic;

using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.DeadLetter;

using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Outbox;

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
        Type? consumerType = null)
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
        foreach (var entry in entries)
        {
            State.Outbox = State.Outbox.Enqueue(entry);
        }

        await _state.WriteStateAsync();
        await DrainAsync();
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
        CancellationToken ct = default)
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
        var results = await Task.WhenAll(events.Select(evt => policy.ApplyAsync(evt, ct)));

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

            var tasks = new Task<Exception?>[ready.Length];
            for (var i = 0; i < ready.Length; i++)
            {
                var entry = ready[i];
                EdictEvent? live = null;
                liveRefs?.TryGetValue(entry.EntryId, out live);
                tasks[i] = ExecuteCapturingAsync(entry, live);
            }

            var outcomes = await Task.WhenAll(tasks);

            var anyTailAppend = false;
            for (var i = 0; i < ready.Length; i++)
            {
                var entry = ready[i];
                var exception = outcomes[i];

                if (exception is null)
                {
                    State.Outbox = State.Outbox.Ack(entry.EntryId);
                    dirty = true;
                    continue;
                }

                // Post-commit failure: do not roll back, do not surface.
                // Bump backoff; if attempts are now exhausted, Promote: the
                // failing entry is removed and an EdictDeadLetterRaised
                // PublishEvent entry is appended at the tail (atomic by
                // construction). The outer loop picks up the tail entry in
                // the next pass.
                State.Outbox = State.Outbox.FailWithBackoff(entry.EntryId, now, _options);

                var bumped = State.Outbox.Pending.FirstOrDefault(p => p.EntryId == entry.EntryId);
                if (bumped is not null && bumped.AttemptCount >= _options.OutboxMaxAttempts)
                {
                    var promoted = _promoter.Promote(
                        bumped, exception, _grainKey, _grainTypeName, now);
                    State.Outbox = State.Outbox.Promote(entry.EntryId, promoted);
                    await _state.WriteStateAsync();
                    dirty = false;
                    anyTailAppend = true;
                    continue;
                }

                await _state.WriteStateAsync();
                dirty = false;
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
            await _state.WriteStateAsync();
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

    async Task<Exception?> ExecuteCapturingAsync(OutboxEntry entry, EdictEvent? liveWireEvent)
    {
        try
        {
            await _executors[entry.Kind].ExecuteAsync(
                entry, _streamProvider, _deferredDispatch, _consumerType, liveWireEvent);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
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
