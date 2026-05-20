using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;

using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Outbox;

/// <summary>
/// The single Outbox component (ADR 0018 / 0022): owns the persisted
/// <see cref="GrainEnvelope{TPayload}"/>, the drain algorithm (FIFO,
/// stop-at-head, exponential backoff, max-attempts dead-letter promotion),
/// and the lazy drain Reminder lifecycle. Lives as a field on each
/// consumer-facing grain shell (<c>EdictCommandHandler&lt;TState&gt;</c>,
/// <c>EdictIdempotencyBase&lt;TPayload&gt;</c>); the shell is a thin Orleans
/// lifecycle adapter that forwards <c>OnActivateAsync</c> and
/// <c>ReceiveReminder</c> to this component. Bare-named — no consumer types
/// it.
/// <para>
/// Drain is <b>FIFO, stop-at-head</b> (per-aggregate causal order) and
/// <b>awaited inline immediately after the commit</b>, so the happy-path span
/// tree is identical to the pre-Outbox code. A post-commit effect failure
/// bumps backoff, stops at the head, and leaves the Reminder as the durable
/// retry — it never rolls back nor surfaces to the caller. At
/// <see cref="EdictOutboxOptions.MaxAttempts"/> the host promotes the failing
/// head via <see cref="IDeadLetterPromoter"/>: the failing entry is removed
/// and a new <see cref="OutboxEffectKind.PublishEvent"/> entry carrying an
/// <c>EdictDeadLetterRaised</c> notification is appended at the FIFO tail, in
/// the same one grain-state write — there is no in-grain dead-letter slice.
/// </para>
/// </summary>
sealed class OutboxHost<TPayload>
    where TPayload : new()
{
    internal const string DrainReminderName = "edict-outbox-drain";

    readonly IPersistentState<GrainEnvelope<TPayload>> _state;
    readonly IStreamProvider _streamProvider;
    readonly IReminderRegistrar _reminders;
    readonly IReadOnlyDictionary<OutboxEffectKind, IOutboxEffectExecutor> _executors;
    readonly EdictOutboxOptions _options;
    readonly TimeProvider _timeProvider;
    readonly IDeadLetterPromoter _promoter;
    readonly Func<EdictEvent, Task>? _deferredDispatch;
    readonly string _grainKey;
    readonly string _grainTypeName;

    bool _drainReminderRegistered;

    public OutboxHost(
        IPersistentState<GrainEnvelope<TPayload>> state,
        IStreamProvider streamProvider,
        IReminderRegistrar reminders,
        IEnumerable<IOutboxEffectExecutor> executors,
        EdictOutboxOptions options,
        TimeProvider timeProvider,
        IDeadLetterPromoter promoter,
        string grainKey,
        string grainTypeName,
        Func<EdictEvent, Task>? deferredDispatch = null)
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
    }

    /// <summary>The persisted envelope <c>{ Payload, Outbox, Idempotency }</c>.</summary>
    public GrainEnvelope<TPayload> State => _state.State;

    /// <summary>Drain-on-activation: catches anything left from a crash before the grain serves traffic (ADR 0018).</summary>
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
    /// Drains pending effects FIFO, stopping at the head on the first failure
    /// or backoff gate. Reconciles the lazy Reminder: unregistered when the
    /// Outbox fully drains, registered while anything remains.
    /// </summary>
    public async Task DrainAsync()
    {
        while (State.Outbox.Pending.Count > 0)
        {
            var head = State.Outbox.Pending[0];
            var now = _timeProvider.GetUtcNow();

            if (head.NextAttemptUtc > now)
            {
                break; // backoff-gated; stop-at-head
            }

            try
            {
                await _executors[head.Kind].ExecuteAsync(head, _streamProvider, _deferredDispatch);
            }
            catch (Exception exception)
            {
                // Post-commit failure: do not roll back, do not surface. Bump
                // backoff; if attempts are now exhausted, promote the head in
                // the SAME one commit — the failing entry is removed and an
                // EdictDeadLetterRaised PublishEvent entry is appended at the
                // FIFO tail (atomic by construction, ADR 0022) and CONTINUE —
                // the poison head has left the FIFO, so the tail is no longer
                // blocked (self-healing). Otherwise stop at the head (causal
                // order) and let the lazy Reminder retry once backoff elapses.
                State.Outbox = State.Outbox.FailHeadWithBackoff(now, _options);

                if (State.Outbox.Pending[0].AttemptCount >= _options.MaxAttempts)
                {
                    var promoted = _promoter.Promote(
                        State.Outbox.Pending[0], exception, _grainKey, _grainTypeName, now);
                    State.Outbox = State.Outbox.PromoteHead(promoted);
                    await _state.WriteStateAsync();
                    continue;
                }

                await _state.WriteStateAsync();
                break;
            }

            State.Outbox = State.Outbox.AckHead();
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

    async Task RegisterDrainReminderAsync()
    {
        await _reminders.RegisterOrUpdateReminderAsync(
            DrainReminderName, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
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
