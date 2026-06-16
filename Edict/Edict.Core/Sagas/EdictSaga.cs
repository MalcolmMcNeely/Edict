using System.Diagnostics;

using Edict.Contracts.Commands;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.Schedules;
using Edict.Core.DeadLetter;
using Edict.Core.Idempotency;
using Edict.Core.Metrics;
using Edict.Core.Outbox;
using Edict.Core.Schedules;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Sagas;

/// <summary>
/// Base for a saga: an idempotent consumer that coordinates a multi-step
/// cross-aggregate workflow, reacting to Events and issuing at most one Command
/// per Event while holding durable <see cref="Progress"/>. It closes
/// the generic idempotency root on <typeparamref name="TProgress"/>, so the
/// "Event Handlers, Sagas, and Projection Builders all inherit
/// <see cref="EdictIdempotencyBase{TPayload}"/>" relationship — and brand
/// clause (b) — stays literally true.
/// <para>
/// The dedup ring, the outbound Command (a <see cref="OutboxEffectKind.SendCommand"/>
/// effect), and <see cref="Progress"/> commit atomically in the one
/// grain-document write (the inherited <see cref="EdictIdempotencyBase{TPayload}.CollectPendingOutboxEntries"/>
/// hook routes the commit through the Outbox engine), so a crash mid-workflow
/// cannot desynchronise progress from the command it implies.
/// </para>
/// <para>
/// <see cref="Dispatch"/> is a deliberately single-command API: command fan-out
/// from a saga is a coordination smell, so a second call within one event
/// handler is a hard runtime error — structurally unmissable rather than
/// advisory, and a deliberate asymmetry with the Command Handler's buffering
/// <c>Raise</c> (no analyzer, consistent with <c>Raise</c> having none).
/// </para>
/// <para>
/// A saga carries an absolute lifetime cap, armed once on its first handled
/// Event and never reset by later activity. The cap, <see cref="Complete"/>,
/// and the terminal-state guard are wired here over the two pure deep modules
/// <see cref="SagaDeadlineResolver"/> and <see cref="SagaLifecycleTransition"/>.
/// </para>
/// </summary>
public abstract class EdictSaga<TProgress> : EdictIdempotencyBase<TProgress>, IEdictSaga, IEdictScheduleFireable
    where TProgress : IEdictPersistedState, new()
{
    // Sibling to OutboxHost.DrainReminderName: the cap reminder is a second
    // lazy reminder on the same subsystem, routed by name in ReceiveReminder.
    internal const string CapReminderName = "edict-saga-cap";

    readonly InvocationScope<SagaDispatchBuffer> _dispatch = new();
    Serializer? _cachedSerializer;
    IEdictMetricsCache? _cachedMetricsCache;
    TimeProvider? _cachedTimeProvider;
    EdictOptions? _cachedOptions;
    EdictSagaOptions? _cachedSagaOptions;
    IReminderRegistrar? _capReminders;
    ScheduleHost<TProgress>? _scheduleHost;
    IDeadLetterPromoter? _deadLetterPromoter;
    string? _cachedSagaType;
    string? _cachedSagaKey;

    SagaLifecycle? _pendingLifecycle;
    SagaLifecycle? _lifecycleBeforeApply;

    enum CapAction
    {
        None,
        Register,
        Unregister,
    }

    /// <summary>
    /// Durable workflow progress. The consumer mutates this inside a
    /// <c>HandleAsync</c>; it is the payload slot of the persisted envelope,
    /// committed atomically with the dedup ring and any dispatched command.
    /// </summary>
    protected TProgress Progress => State.Payload;

    string SagaType => _cachedSagaType ??= GetType().FullName ?? GetType().Name;
    string SagaKey => _cachedSagaKey ??= this.GetPrimaryKey().ToString();
    TimeProvider Clock => _cachedTimeProvider ??= ServiceProvider.GetRequiredService<TimeProvider>();
    EdictOptions Options => _cachedOptions ??= ServiceProvider.GetRequiredService<IOptions<EdictOptions>>().Value;
    EdictSagaOptions SagaOptions =>
        _cachedSagaOptions ??= ServiceProvider.GetService<IOptions<EdictSagaOptions>>()?.Value ?? new EdictSagaOptions();
    IReminderRegistrar CapReminders => _capReminders ??= new GrainReminderRegistrar(this);
    Serializer Serializer => _cachedSerializer ??= ServiceProvider.GetRequiredService<Serializer>();
    ScheduleHost<TProgress> ScheduleHost => _scheduleHost ??= BuildScheduleHost();
    IDeadLetterPromoter DeadLetterPromoter => _deadLetterPromoter ??= ServiceProvider.GetRequiredService<IDeadLetterPromoter>();

    /// <inheritdoc cref="IEdictSaga.GetEdictProgressAsync" />
    public Task<object> GetEdictProgressAsync() => Task.FromResult<object>(Progress!);

    /// <summary>
    /// Issues the single Command this Event implies. Buffered now and staged as
    /// the <see cref="OutboxEffectKind.SendCommand"/> effect after the handler
    /// succeeds, so it commits atomically with the ring and
    /// <see cref="Progress"/>. Calling this a second time within one event
    /// handler throws — a saga that fans out commands is a coordination smell,
    /// and the single-command API shape makes that constraint structural.
    /// </summary>
    protected void Dispatch(EdictCommand command) => _dispatch.Current.Set(command);

    /// <summary>
    /// Generator-only fast path called by the per-type saga Dispatch
    /// interceptor stubs. Identical semantics to
    /// <see cref="Dispatch"/> on the typed argument — the win is a
    /// monomorphic typed call site. Not a stable public API; the interceptor
    /// emitter is the only caller. The single-command-per-event invariant
    /// (<see cref="SagaDispatchBuffer.Set"/> throws on a second call) still
    /// holds.
    /// </summary>
    public void DispatchFast<TCommand>(TCommand command) where TCommand : EdictCommand
        => _dispatch.Current.Set(command);

    /// <summary>
    /// Marks the saga successfully finished. Hard-terminal: the lifecycle moves
    /// to <see cref="SagaLifecycleState.Completed"/> in the same atomic write as
    /// the handler's progress, the cap reminder is unregistered, and any later
    /// genuinely-new Event dead-letters. Opt-in — a saga whose key may
    /// legitimately receive a later Event simply never calls it. Symmetric with
    /// <see cref="Dispatch"/>: buffered now, applied at the commit.
    /// </summary>
    protected void Complete() => _dispatch.Current.RequestComplete();

    /// <summary>
    /// Starts a recurring schedule from inside an event <c>HandleAsync</c>. The
    /// surface is identical to a Command Handler's: the <paramref name="message"/>
    /// is persisted (data plus its <c>[Alias]</c>), never a delegate, and each fire
    /// deserialises it and routes it back through this saga's
    /// <c>HandleAsync(TMessage) : Task&lt;EdictScheduleResult&gt;</c>, re-entering the
    /// full handler lifecycle so a fire can <see cref="Dispatch"/> a Command (poll
    /// then send). The first fire is at <c>+every</c>; the handler returns
    /// <see cref="EdictScheduleResult.Continue"/> or
    /// <see cref="EdictScheduleResult.Complete"/>.
    /// <para>
    /// A saga schedule is bounded by the saga's own timeout: the command-handler
    /// silo default does not apply inside a saga, so the saga cap stays the single
    /// ceiling for everything a saga owns. An explicit positive
    /// <paramref name="timeout"/> caps this one schedule shorter (armed once here,
    /// never reset by a fire); <see cref="EdictSchedule.Unbounded"/> and omitting
    /// <paramref name="timeout"/> both leave the schedule uncapped at the schedule
    /// level. On a fired cap the schedule runs <c>OnScheduleTimeoutAsync(TMessage)</c>
    /// if one is written, else dead-letters.
    /// </para>
    /// </summary>
    protected void Schedule(EdictScheduleMessage message, TimeSpan every, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        var armingSpan = Activity.Current;
        ScheduleHost.Schedule(
            Serializer.SerializeToArray(message),
            every,
            ResolveScheduleCap(timeout),
            armingSpan?.BuildTraceParent(),
            armingSpan?.TraceStateString,
            PrincipalRelay.Current(),
            TenantRelay.Current());
    }

    // Resolves the effective timeout duration for a saga schedule. Unlike a Command
    // Handler's, this never inherits EdictCommandHandlerScheduleOptions.DefaultTimeout:
    // the saga's own absolute cap is the single ceiling. An explicit timeout: caps
    // this schedule shorter; EdictSchedule.Unbounded and an omitted timeout: both
    // leave it uncapped at the schedule level (null).
    static TimeSpan? ResolveScheduleCap(TimeSpan? timeout) =>
        timeout is { } explicitTimeout && explicitTimeout != EdictSchedule.Unbounded
            ? explicitTimeout
            : null;

    /// <summary>
    /// Generator override target: type-switches a fired message to the saga's
    /// matching <c>HandleAsync(TMessage)</c>. The base default is unreachable — a
    /// schedule can only be armed for a message this saga dispatches.
    /// </summary>
    protected virtual Task<EdictScheduleResult> DispatchScheduleFireAsync(EdictScheduleMessage message) =>
        throw new EdictUnroutableScheduleMessageException(message.GetType());

    /// <summary>
    /// Generator override target: type-switches a timed-out message to the saga's
    /// matching <c>OnScheduleTimeoutAsync(TMessage)</c> compensation hook, returning
    /// <c>true</c> when one ran. The base default returns <c>false</c> for every
    /// message — a schedule whose author wrote no hook dead-letters its fired cap.
    /// </summary>
    protected virtual Task<bool> DispatchScheduleTimeoutAsync(EdictScheduleMessage message) =>
        Task.FromResult(false);

    /// <inheritdoc />
    public Task FireDueSchedulesAsync() => ScheduleHost.FireDueAsync();

    /// <inheritdoc />
    public Task<DateTimeOffset?> PeekSoonestScheduleDueAsync() => Task.FromResult(ScheduleHost.SoonestDueAt);

    /// <inheritdoc />
    public Task FireDueScheduleTimeoutsAsync() => ScheduleHost.FireTimeoutsAsync();

    /// <inheritdoc />
    public Task<DateTimeOffset?> PeekSoonestScheduleTimeoutAsync() => Task.FromResult(ScheduleHost.SoonestTimeoutAt);

    /// <summary>
    /// Whether this saga declares a <c>HandleAsync</c> overload for the runtime
    /// type of <paramref name="edictEvent"/>. The generator emits the override as
    /// the same closed switch its <c>DispatchAsync</c> dispatches over. A saga
    /// subscribes to a whole domain stream, so it receives event types it does
    /// not handle; those are ignored on the live path (no matching arm) and must
    /// be ignored at a terminal saga too: only handled types dead-letter there.
    /// </summary>
    protected abstract bool HandlesEventType(EdictEvent edictEvent);

    /// <summary>
    /// Saga stream-callback path: dedup first (an already-handled redelivery is
    /// suppressed regardless of terminal state), then the terminal-state guard
    /// (a genuinely-new Event the saga handles dead-letters at a terminal saga;
    /// an unhandled event type off the shared stream is ignored), then the
    /// handler — whose commit also arms the cap on the first handle or
    /// terminalises it when the handler called <see cref="Complete"/>.
    /// </summary>
    protected override Task OnStreamEventAsync(EdictEvent edictEvent, StreamSequenceToken? _) =>
        GuardDedupClaimAsync(edictEvent, async () =>
        {
            var lifecycle = State.Saga;
            var currentState = lifecycle?.State ?? SagaLifecycleState.Live;

            if (HandlesEventType(edictEvent)
                && SagaLifecycleTransition.Resolve(currentState, SagaTrigger.NewEvent) == SagaTransitionDecision.DeadLetterTerminal)
            {
                // Consume the ring slot (so this exact event is not redelivered
                // forever) and stage a dead-letter in one write; the lifecycle
                // stays terminal.
                var terminalDeadLetter = BuildSagaDeadLetterEntry(new EdictSagaTerminalException(
                    $"Saga '{SagaType}' received '{edictEvent.GetType().Name}' after it became terminal ({currentState})."));
                _pendingLifecycle = null;
                await CommitAndPersistAsync(edictEvent.EventId, terminalDeadLetter);
                return;
            }

            var outcome = await DispatchAsync(edictEvent);
            if (!outcome.Handled)
            {
                return;
            }

            var (newLifecycle, capAction) = NextLifecycle(lifecycle, outcome.CompleteRequested, edictEvent);
            _pendingLifecycle = newLifecycle;
            await CommitAndPersistAsync(edictEvent.EventId, outcome.StagedEffect);
            _pendingLifecycle = null;

            // Count the completion only once the terminal transition is durable; the
            // dedup ring and terminal guard above make this exactly-once per saga.
            if (newLifecycle?.State == SagaLifecycleState.Completed)
            {
                SagaLifecycleMetrics.EmitCompleted(SagaType);
            }

            await ApplyCapReminderActionAsync(capAction, newLifecycle);

            // A handler that called Schedule(...) staged an entry the commit above just
            // made durable; arm its timer and Reminder now.
            await ReconcileScheduleIfActiveAsync();
        });

    /// <summary>
    /// Deferred (pointer-envelope) intake: dedup-first → arm → stage, with the
    /// handler running later off the engine drain. Arming folds into the staging
    /// write (it needs only the fact of receipt, not the oversized body), so an
    /// oversized first Event still arms the absolute cap. The terminal guard,
    /// unlike the inline path, runs in <see cref="DispatchDeferredAsync"/> once the
    /// body is materialised: it has to know the event type to tell a handled
    /// Event (dead-letter at a terminal saga) from a foreign one off the shared
    /// stream (ignore). Arming anchors at receipt here (the handler runs after the
    /// drain), and the cap reminder is registered once the staging commit and the
    /// drain it triggers have settled, so a <see cref="Complete"/> the deferred
    /// handler called wins over a needless registration.
    /// </summary>
    private protected override Task StagePointerEnvelopeForDeferredDispatchAsync(EdictEventEnvelope envelope) =>
        GuardDedupClaimAsync(envelope, async () =>
        {
            var lifecycle = State.Saga;
            var priorState = lifecycle?.State;

            // The terminal guard cannot run here: the body is still in the claim-check
            // store, so the event type is unknown until the InvokeHandler entry
            // materialises it on the drain. Stage the entry unconditionally and let
            // the type-aware terminal decision happen in DispatchDeferredAsync, where
            // the body is in hand: an unhandled type off the shared stream is ignored
            // rather than dead-lettered.
            var (newLifecycle, capAction) = NextLifecycle(lifecycle, completeRequested: false, envelope);
            _pendingLifecycle = newLifecycle;
            await CommitAndPersistAsync(envelope.EventId, BuildInvokeHandlerEntry(envelope));
            _pendingLifecycle = null;

            if (State.Saga?.State == SagaLifecycleState.Completed && priorState != SagaLifecycleState.Completed)
            {
                // The deferred handler called Complete() during the drain the staging
                // commit kicked off; count and disarm only on that transition, not when
                // the saga was already terminal before this event arrived.
                SagaLifecycleMetrics.EmitCompleted(SagaType);
                await CapReminders.UnregisterReminderAsync(CapReminderName);
            }
            else if (capAction == CapAction.Register && State.Saga is { State: SagaLifecycleState.Live } live)
            {
                await ApplyCapReminderActionAsync(CapAction.Register, live);
            }
        });

    /// <summary>
    /// Deferred-dispatch callback: runs the handler off the engine drain and, when
    /// it called <see cref="Complete"/>, terminalises the lifecycle slot in place
    /// so the <see cref="SagaLifecycleState.Completed"/> write rides the engine's
    /// <see cref="OutboxEffectKind.InvokeHandler"/> ack-write alongside any
    /// compensating Command. The staging caller emits the completed metric and
    /// disarms the cap once that write is durable.
    /// </summary>
    private protected override async Task<OutboxEntry?> DispatchDeferredAsync(EdictEvent edictEvent)
    {
        var currentState = State.Saga?.State ?? SagaLifecycleState.Live;

        if (SagaLifecycleTransition.Resolve(currentState, SagaTrigger.NewEvent) == SagaTransitionDecision.DeadLetterTerminal)
        {
            // Terminal, and the body is now materialised: apply the same
            // type-aware guard the inline path does. A handled type dead-letters;
            // an unhandled type off the shared stream is ignored. The handler
            // never runs either way.
            return HandlesEventType(edictEvent)
                ? BuildSagaDeadLetterEntry(new EdictSagaTerminalException(
                    $"Saga '{SagaType}' received '{edictEvent.GetType().Name}' after it became terminal ({currentState})."))
                : null;
        }

        var outcome = await DispatchAsync(edictEvent);
        if (outcome.CompleteRequested && State.Saga is { State: not SagaLifecycleState.Completed } current)
        {
            State.Saga = current with { State = SagaLifecycleState.Completed };
        }
        return outcome.StagedEffect;
    }

    /// <summary>
    /// Opens a fresh single-command buffer for this Event so the
    /// one-command-per-event limit is scoped to one Event (and isolated from
    /// any concurrently-draining dispatch), runs the handler, then returns the
    /// buffered command as a <see cref="OutboxEffectKind.SendCommand"/> effect
    /// alongside whether the handler called <see cref="Complete"/>.
    /// </summary>
    protected override async Task<EdictDispatchOutcome> DispatchEventAsync<TEvent>(TEvent edictEvent, Func<TEvent, Task> handler)
    {
        var buffer = _dispatch.Begin();

        // Seed the turn's principal and tenant off the handled event so a Dispatch
        // the handler issues, or a Schedule it arms, inherits the originating actor
        // and stays inside the same tenant wall through the relay rather than
        // threading the durable fields by hand.
        OriginIdentity.Seed(edictEvent.Principal, edictEvent.Tenant);

        await handler(edictEvent);

        // Build the SendCommand entry here, while the handle span is still
        // Activity.Current, so its captured traceparent makes the dispatched
        // command nest under the saga handle span as parent-child even when a
        // crash-recovery drain runs much later. Stamp the handled event's
        // correlation onto the outbound command so the chain survives the hop;
        // SendAsync's mint-if-empty then honours it rather than starting anew. The
        // principal and tenant ride off the same relay so the command stays
        // attributed to the actor whose event triggered it and inside that event's
        // tenant wall, both carried durably in the serialized effect.
        var command = buffer.Take();
        var effect = command is null
            ? null
            : BuildSendCommandEntry(command with { CorrelationId = edictEvent.CorrelationId, Principal = PrincipalRelay.Current(), Tenant = TenantRelay.Current() });

        ReportSagaProgress();

        return EdictDispatchOutcome.HandledWith(effect, buffer.CompleteRequested);
    }

    /// <summary>
    /// Compensation hook invoked when the absolute lifetime cap fires. Override
    /// to run compensation with full access to <see cref="Progress"/>: mutate it
    /// and <see cref="Dispatch"/> at most one compensating Command, both of which
    /// commit atomically with the <see cref="SagaLifecycleState.TimedOut"/>
    /// terminal write. The default implementation routes the fired cap to
    /// dead-letter, so a forgotten timeout surfaces loudly rather than silently
    /// stranding the workflow.
    /// </summary>
    protected virtual Task OnSagaTimeoutAsync()
    {
        _dispatch.Current.RequestDeadLetter();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task ReceiveReminder(string reminderName, TickStatus status) =>
        reminderName switch
        {
            CapReminderName => ReceiveCapReminderAsync(),
            ScheduleHost<TProgress>.ScheduleReminderName => ScheduleHost.ReceiveReminderAsync(),
            _ => base.ReceiveReminder(reminderName, status),
        };

    /// <summary>
    /// Re-arms any durable schedule's timer and Reminder when the saga activates,
    /// on top of the base's outbox drain-on-activation. Skipped entirely for the
    /// common saga that never scheduled, so the cost is one Count check.
    /// </summary>
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        if (State.Schedule.Active.Count > 0)
        {
            await ScheduleHost.OnActivateAsync();
        }
    }

    /// <summary>
    /// The cap reminder tick. Terminalises a live saga: it opens a fresh dispatch
    /// buffer from the cap branch (no incoming Event, so no dedup-ring slot),
    /// invokes <see cref="OnSagaTimeoutAsync"/>, and commits the
    /// <see cref="SagaLifecycleState.TimedOut"/> write atomically with whichever
    /// effect the hook implied — the override's single compensating Command, or
    /// the default's dead-letter. It is an idempotent no-op against an
    /// already-terminal saga, so a non-transactional reminder double-fire cannot
    /// double-act. Internal so an in-memory lifecycle test can drive the fire
    /// deterministically without waiting on Orleans' one-minute reminder floor.
    /// </summary>
    internal async Task ReceiveCapReminderAsync()
    {
        var lifecycle = State.Saga;
        var currentState = lifecycle?.State ?? SagaLifecycleState.Live;

        if (SagaLifecycleTransition.Resolve(currentState, SagaTrigger.CapFired) == SagaTransitionDecision.NoOp)
        {
            await CapReminders.UnregisterReminderAsync(CapReminderName);
            return;
        }

        // Early fire (clock skew, or the reminder floor pulled the first tick
        // before the deadline): not yet due, so leave the reminder ticking.
        if (lifecycle?.DeadlineAt is { } deadline && Clock.GetUtcNow() < deadline)
        {
            return;
        }

        // The fired cap is its own grain turn: a new trace root linking back to the
        // context that armed the saga. As the current span it makes a compensating
        // command's edict.command.send (or the dead-letter publish) nest under it.
        var link = ActivityExtensions.BuildLink(lifecycle?.ArmTraceParent, lifecycle?.ArmTraceState);
        using var timeoutSpan = EdictDiagnostics.ActivitySource.StartEdictSagaTimeout(GetType().Name, link);

        var buffer = _dispatch.Begin();

        OutboxEntry? stagedEffect;
        string outcome;
        try
        {
            await OnSagaTimeoutAsync();
            var command = buffer.Take();

            // The override dispatched a compensating Command; otherwise the default
            // hook requested a dead-letter; otherwise (an override that only mutated
            // Progress) the terminal write rides alone.
            stagedEffect =
                command is not null ? BuildSendCommandEntry(command)
                : buffer.DeadLetterRequested ? BuildSagaDeadLetterEntry(new EdictSagaTimeoutException(
                    $"Saga '{SagaType}' hit its absolute lifetime cap with no OnSagaTimeoutAsync override; the timeout was dead-lettered."))
                : null;

            outcome = command is not null
                ? SemanticConventions.Sagas.Tags.OutcomeValues.Compensated
                : SemanticConventions.Sagas.Tags.OutcomeValues.Deadlettered;
        }
        catch (Exception compensationFault)
        {
            // A consumer's OnSagaTimeoutAsync override threw. Discard its half-applied
            // Progress mutation by reloading the durable snapshot (the dispatch buffer
            // self-heals on the next InvocationScope.Begin), then terminalise and
            // dead-letter as a ConsumerBug so a faulting compensation reads apart from
            // the by-design no-override SagaTimeout. Containing the throw here is what
            // stops the cap reminder re-throwing it on every tick and stranding the
            // workflow Live forever.
            await ReadStateAsync();
            stagedEffect = BuildSagaDeadLetterEntry(new EdictSagaCompensationException(
                $"Saga '{SagaType}' OnSagaTimeoutAsync override threw {compensationFault.GetType().FullName}: {compensationFault.Message}",
                compensationFault));
            outcome = SemanticConventions.Sagas.Tags.OutcomeValues.CompensationFailed;
        }

        var timedOut = (lifecycle ?? new SagaLifecycle()) with { State = SagaLifecycleState.TimedOut };

        await CommitLifecycleOnlyAsync(timedOut, stagedEffect);

        // The fired cap is now durable: count it with the outcome the hook chose —
        // compensated when it dispatched a Command, deadlettered when it requested
        // one, or compensation_failed when the override threw and was contained.
        SagaLifecycleMetrics.EmitTimeoutFired(SagaType, outcome);

        await CapReminders.UnregisterReminderAsync(CapReminderName);
    }

    private protected override void ApplyConsumerProgress()
    {
        if (_pendingLifecycle is not null)
        {
            _lifecycleBeforeApply = State.Saga;
            State.Saga = _pendingLifecycle;
        }
    }

    private protected override void RollbackConsumerProgress()
    {
        if (_pendingLifecycle is not null)
        {
            State.Saga = _lifecycleBeforeApply;
        }
    }

    (SagaLifecycle? Lifecycle, CapAction Action) NextLifecycle(SagaLifecycle? existing, bool completeRequested, EdictEvent armingEvent)
    {
        if (existing is null)
        {
            // First handled Event: arm the absolute cap anchored here, and capture
            // the arming event's trace context so a fired cap links back to it.
            var declaration = SagaTimeoutAttributeReader.Read(GetType());
            var deadline = SagaDeadlineResolver.Resolve(declaration, SagaOptions.DefaultTimeout, Clock.GetUtcNow());
            var (armTraceParent, armTraceState) = ArmContextFrom(armingEvent);

            if (completeRequested)
            {
                // Completed inside its very first handler — never arm a cap.
                return (new SagaLifecycle
                {
                    State = SagaLifecycleState.Completed,
                    DeadlineAt = deadline,
                    ArmTraceParent = armTraceParent,
                    ArmTraceState = armTraceState,
                }, CapAction.None);
            }

            var armed = new SagaLifecycle
            {
                State = SagaLifecycleState.Live,
                DeadlineAt = deadline,
                ArmTraceParent = armTraceParent,
                ArmTraceState = armTraceState,
            };
            return (armed, deadline is null ? CapAction.None : CapAction.Register);
        }

        if (completeRequested)
        {
            return (existing with { State = SagaLifecycleState.Completed }, CapAction.Unregister);
        }

        // The absolute cap is never reset by later activity, so a normal
        // subsequent handle leaves the lifecycle untouched.
        return (null, CapAction.None);
    }

    // The arming event's stamped publish context, as a W3C traceparent the fired cap
    // links back to. Null when the event carried no trace (unsampled or untraced).
    static (string? TraceParent, string? TraceState) ArmContextFrom(EdictEvent armingEvent) =>
        armingEvent.TraceId is { Length: 32 } traceId && armingEvent.SpanId is { Length: 16 } spanId
            ? (ActivityExtensions.BuildTraceParent(traceId, spanId), armingEvent.TraceState)
            : (null, null);

    async Task ApplyCapReminderActionAsync(CapAction action, SagaLifecycle? lifecycle)
    {
        switch (action)
        {
            case CapAction.Register when lifecycle?.DeadlineAt is { } deadline:
                // Reuse the silo's reminder cadence (validated >= the Orleans
                // one-minute floor) as both the cap reminder period and the
                // due-time floor, so a sub-floor remaining time still registers.
                var period = Options.OutboxDrainReminderPeriod;
                var dueTime = deadline - Clock.GetUtcNow();
                if (dueTime < period)
                {
                    dueTime = period;
                }
                await CapReminders.RegisterOrUpdateReminderAsync(CapReminderName, dueTime, period);
                break;

            case CapAction.Unregister:
                await CapReminders.UnregisterReminderAsync(CapReminderName);
                break;
        }
    }

    Task CommitLifecycleOnlyAsync(SagaLifecycle newLifecycle, OutboxEntry? stagedEffect)
    {
        // No incoming Event, so no dedup-ring slot: just the lifecycle write and
        // any staged effect (compensating Command or dead-letter), plus any
        // in-place Progress mutation the hook made, atomic in one grain-state write.
        var previous = State.Saga;
        return Host.CommitProgressAndDrainAsync(
            applyProgress: () => State.Saga = newLifecycle,
            rollbackProgress: () => State.Saga = previous,
            stagedEffect: stagedEffect);
    }

    void ReportSagaProgress()
    {
        var cache = _cachedMetricsCache ??= ServiceProvider.GetService<IEdictMetricsCache>();
        if (cache is null)
        {
            return;
        }
        cache.ReportSaga(
            sagaType: SagaType,
            sagaKey: SagaKey,
            lastHandledAt: Clock.GetUtcNow());
    }

    // The dispatch delegate ScheduleHost calls per due entry — the saga's schedule
    // fire lifecycle, parallel to the Command Handler's. A fresh dispatch buffer
    // scopes the one-command-per-fire limit, the fire handler runs through the
    // generated DispatchScheduleFireAsync, and the schedule's next-due advance
    // commits atomically with any Dispatched Command (poll then send) through the
    // saga's own Outbox commit boundary — no dedup-ring slot, since no Event came
    // in. A handler throw discards the turn (partial Progress rolled back to the
    // last durable snapshot, the buffered command dropped) but re-times the
    // schedule forward so a failing poll retries on the next cadence. Once the saga
    // is terminal its schedules stop: the saga cap is the single ceiling for
    // everything a saga owns, so a fire against a Completed or TimedOut saga removes
    // the schedule rather than running the handler.
    async Task FireScheduleEntryAsync(ScheduleEntry entry, DateTimeOffset now)
    {
        if (IsTerminal())
        {
            await PruneScheduleAsync(entry.ScheduleId);
            return;
        }

        var message = Serializer.Deserialize<EdictScheduleMessage>(entry.MessagePayload);
        var buffer = _dispatch.Begin();

        // The fire is its own grain turn: a new trace root linking back to the event
        // that armed the schedule. As the current context it makes a Dispatched
        // command's edict.command.send nest under the fire within the turn.
        var link = ActivityExtensions.BuildLink(entry.ArmTraceParent, entry.ArmTraceState);
        using var fireSpan = EdictDiagnostics.ActivitySource.StartEdictScheduleFire(message.GetType().Name, link);
        fireSpan?.CaptureToRequestContext();
        OriginIdentity.Seed(entry.ArmPrincipal, entry.ArmTenant);

        EdictScheduleResult result;
        try
        {
            result = await DispatchScheduleFireAsync(message);
        }
        catch
        {
            await ReadStateAsync();
            await CommitScheduleAsync(base.State.Schedule.Continue(entry.ScheduleId, now), stagedEffect: null);
            return;
        }

        // A fire stays attributed to the arming principal and tenant carried durably
        // in the entry, so a Dispatched poll-then-send command is ascribed to whoever
        // armed the schedule, inside that tenant wall, even across a reactivation.
        var command = buffer.Take();
        var effect = command is null ? null : BuildSendCommandEntry(command with { Principal = entry.ArmPrincipal, Tenant = entry.ArmTenant });

        var nextSchedule = result is EdictScheduleResult.Complete
            ? base.State.Schedule.Complete(entry.ScheduleId)
            : base.State.Schedule.Continue(entry.ScheduleId, now);

        await CommitScheduleAsync(nextSchedule, effect);
    }

    // The dispatch delegate ScheduleHost calls per timed-out entry. A timeout is
    // terminal: it removes the schedule in the same commit as its outcome. A written
    // OnScheduleTimeoutAsync hook runs as compensation and may Dispatch one
    // compensating Command (which commits atomically with the removal); otherwise the
    // cap dead-letters through DeadLetterPromoter with a no-throw synthetic row. A
    // hook that throws is rolled back to the durable snapshot and dead-lettered.
    async Task FireScheduleTimeoutEntryAsync(ScheduleEntry entry, DateTimeOffset now)
    {
        if (IsTerminal())
        {
            await PruneScheduleAsync(entry.ScheduleId);
            return;
        }

        var message = Serializer.Deserialize<EdictScheduleMessage>(entry.MessagePayload);
        var buffer = _dispatch.Begin();

        bool handled;
        try
        {
            handled = await DispatchScheduleTimeoutAsync(message);
        }
        catch
        {
            await ReadStateAsync();
            handled = false;
        }

        if (handled)
        {
            var command = buffer.Take();
            var effect = command is null ? null : BuildSendCommandEntry(command);
            await CommitScheduleAsync(base.State.Schedule.Complete(entry.ScheduleId), effect);
            return;
        }

        var (traceId, spanId, traceState) = ActivityExtensions.ReadRequestContext();
        var traceParent = traceId is not null && spanId is not null
            ? ActivityExtensions.BuildTraceParent(traceId, spanId)
            : null;

        var deadLetter = DeadLetterPromoter.PromoteScheduleTimeout(
            scheduleMessageType: message.GetType().FullName ?? message.GetType().Name,
            sourceGrainKey: SagaKey,
            sourceGrainType: SagaType,
            traceParent: traceParent,
            traceState: traceState,
            now: now);

        await CommitScheduleAsync(base.State.Schedule.Complete(entry.ScheduleId), deadLetter);
    }

    bool IsTerminal() => State.Saga?.State is SagaLifecycleState.Completed or SagaLifecycleState.TimedOut;

    Task PruneScheduleAsync(Guid scheduleId) => CommitScheduleAsync(base.State.Schedule.Complete(scheduleId), stagedEffect: null);

    // Commits a schedule-slice transition plus any staged effect (a Dispatched
    // Command or a dead-letter) atomically through the saga's Outbox commit
    // boundary, then reconciles the timer and Reminder against what the fire left.
    async Task CommitScheduleAsync(ScheduleSlice nextSchedule, OutboxEntry? stagedEffect)
    {
        var before = base.State.Schedule;
        await Host.CommitProgressAndDrainAsync(
            applyProgress: () => base.State.Schedule = nextSchedule,
            rollbackProgress: () => base.State.Schedule = before,
            stagedEffect: stagedEffect);
        await ScheduleHost.ReconcileAsync();
    }

    // Arms the timer and Reminder when a handled Event staged a schedule. Skipped
    // entirely for the common non-scheduling saga (no host built, no durable
    // schedules), so the cost there is one Count check.
    Task ReconcileScheduleIfActiveAsync() =>
        _scheduleHost is not null || State.Schedule.Active.Count > 0
            ? ScheduleHost.ReconcileAsync()
            : Task.CompletedTask;

    ScheduleHost<TProgress> BuildScheduleHost() =>
        new(
            new GrainPersistentStateAdapter<GrainEnvelope<TProgress>>(
                get: () => base.State,
                set: v => base.State = v,
                writeState: WriteStateAsync),
            new GrainReminderRegistrar(this),
            new GrainScheduleTimer(this),
            Clock,
            Options,
            FireScheduleEntryAsync,
            FireScheduleTimeoutEntryAsync);

    OutboxEntry BuildSendCommandEntry(EdictCommand command)
    {
        var current = Activity.Current;
        var traceParent = current?.BuildTraceParent();

        var serializer = _cachedSerializer ??= ServiceProvider.GetRequiredService<Serializer>();

        return new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.SendCommand,
            Payload = serializer.SerializeToArray<EdictCommand>(command),
            TraceParent = traceParent,
            TraceState = current?.TraceStateString,
        };
    }

    OutboxEntry BuildSagaDeadLetterEntry(Exception cause)
    {
        var current = Activity.Current;
        var traceParent = current?.BuildTraceParent();
        var serializer = _cachedSerializer ??= ServiceProvider.GetRequiredService<Serializer>();

        // A saga lifecycle failure has no failing effect to promote, so build
        // the forensic notification directly and publish it on the dead-letter
        // stream the singleton projection consumes — the same row shape the
        // outbox promoter emits, keyed on the stable Edict* exception type.
        var raised = new EdictDeadLetterRaised
        {
            // Own delivery identity, stamped here at origination — the drain no
            // longer mints one, and a fresh id keeps each lifecycle dead-letter a
            // distinct row in the singleton projection's dedup ring.
            EventId = Guid.NewGuid(),
            EntryId = Guid.NewGuid(),
            Kind = cause switch
            {
                EdictSagaTimeoutException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.SagaTimeout,
                EdictSagaCompensationException => SemanticConventions.DeadLetter.Tags.FailureReasonValues.ConsumerBug,
                _ => SemanticConventions.DeadLetter.Tags.FailureReasonValues.SagaTerminal,
            },
            AttemptCount = 0,
            DeadLetteredAt = Clock.GetUtcNow(),
            SourceGrainKey = SagaKey,
            SourceGrainType = SagaType,
            EffectTarget = SagaType,
            TraceParent = traceParent,
            ExceptionType = cause.GetType().FullName,
            Reason = cause.Message,
        };

        return new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.PublishEvent,
            Payload = serializer.SerializeToArray<EdictEvent>(raised),
            TraceParent = traceParent,
            TraceState = current?.TraceStateString,
        };
    }
}
