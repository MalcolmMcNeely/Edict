using System.Diagnostics;

using Edict.Contracts.Commands;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.DeadLetter;
using Edict.Core.Idempotency;
using Edict.Core.Metrics;
using Edict.Core.Outbox;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Sagas;

/// <summary>
/// Base for a saga: an idempotent consumer that coordinates a multi-step
/// cross-aggregate workflow, reacting to Events and issuing exactly one Command
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
public abstract class EdictSaga<TProgress> : EdictIdempotencyBase<TProgress>, IEdictSaga
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
    /// Saga stream-callback path: dedup first (an already-handled redelivery is
    /// suppressed regardless of terminal state), then the terminal-state guard
    /// (a genuinely-new Event at a terminal saga dead-letters), then the
    /// handler — whose commit also arms the cap on the first handle or
    /// terminalises it when the handler called <see cref="Complete"/>.
    /// </summary>
    protected override async Task OnStreamEventAsync(EdictEvent edictEvent, StreamSequenceToken? _)
    {
        EnsureWindowInitialized();

        if (Contains(edictEvent.EventId))
        {
            EmitDedupSpan(edictEvent);
            IdempotencyDedupMetrics.EmitDedupHit(edictEvent, SagaType);
            return;
        }

        var lifecycle = State.Saga;
        var currentState = lifecycle?.State ?? SagaLifecycleState.Live;

        if (SagaLifecycleTransition.Resolve(currentState, SagaTrigger.NewEvent) == SagaTransitionDecision.DeadLetterTerminal)
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

        var (newLifecycle, capAction) = NextLifecycle(lifecycle, outcome.CompleteRequested);
        _pendingLifecycle = newLifecycle;
        await CommitAndPersistAsync(edictEvent.EventId, outcome.StagedEffect);
        _pendingLifecycle = null;

        await ApplyCapReminderActionAsync(capAction, newLifecycle);
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

        await handler(edictEvent);

        // Build the SendCommand entry here, while the handle span is still
        // Activity.Current, so its captured traceparent makes the dispatched
        // command nest under the saga handle span as parent-child even when a
        // crash-recovery drain runs much later.
        var command = buffer.Take();
        var effect = command is null ? null : BuildSendCommandEntry(command);

        ReportSagaProgress();

        return EdictDispatchOutcome.HandledWith(effect, buffer.CompleteRequested);
    }

    /// <inheritdoc />
    public override Task ReceiveReminder(string reminderName, TickStatus status) =>
        reminderName == CapReminderName ? ReceiveCapReminderAsync() : base.ReceiveReminder(reminderName, status);

    /// <summary>
    /// The cap reminder tick. Terminalises a live saga (this slice always
    /// dead-letters — the <c>OnSagaTimeoutAsync</c> compensation override is the
    /// next slice) and is an idempotent no-op against an already-terminal saga,
    /// so a non-transactional reminder double-fire cannot double-act.
    /// Internal so an in-memory lifecycle test can drive the fire deterministically
    /// without waiting on Orleans' one-minute reminder floor.
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

        var timeoutDeadLetter = BuildSagaDeadLetterEntry(new EdictSagaTimeoutException(
            $"Saga '{SagaType}' hit its absolute lifetime cap with no OnSagaTimeoutAsync override; the timeout was dead-lettered."));
        var timedOut = (lifecycle ?? new SagaLifecycle()) with { State = SagaLifecycleState.TimedOut };

        await CommitLifecycleOnlyAsync(timedOut, timeoutDeadLetter);
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

    (SagaLifecycle? Lifecycle, CapAction Action) NextLifecycle(SagaLifecycle? existing, bool completeRequested)
    {
        if (existing is null)
        {
            // First handled Event: arm the absolute cap anchored here.
            var declaration = SagaTimeoutAttributeReader.Read(GetType());
            var deadline = SagaDeadlineResolver.Resolve(declaration, SagaOptions.DefaultTimeout, Clock.GetUtcNow());

            if (completeRequested)
            {
                // Completed inside its very first handler — never arm a cap.
                return (new SagaLifecycle { State = SagaLifecycleState.Completed, DeadlineAt = deadline }, CapAction.None);
            }

            var armed = new SagaLifecycle { State = SagaLifecycleState.Live, DeadlineAt = deadline };
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

    Task CommitLifecycleOnlyAsync(SagaLifecycle newLifecycle, OutboxEntry stagedEffect)
    {
        // No incoming Event, so no dedup-ring slot: just the lifecycle write and
        // the staged dead-letter, atomic in one grain-state write.
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
            EntryId = Guid.NewGuid(),
            Kind = cause is EdictSagaTimeoutException
                ? SemanticConventions.DeadLetter.Tags.FailureReasonValues.SagaTimeout
                : SemanticConventions.DeadLetter.Tags.FailureReasonValues.SagaTerminal,
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
