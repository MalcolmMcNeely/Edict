using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;

using Edict.Contracts;
using Edict.Contracts.Audit;
using Edict.Contracts.Tenancy;
using Edict.Contracts.Commands;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.Schedules;
using Edict.Core.Audit;
using Edict.Core.ClaimCheck;
using Edict.Core.DeadLetter;
using Edict.Core.Metrics;
using Edict.Core.Outbox;
using Edict.Core.Schedules;
using Edict.Telemetry;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Edict.Core.Commands;

/// <summary>
/// Base for an aggregate grain. The framework owns durable aggregate state: the
/// persisted document is the single-write <see cref="GrainEnvelope{TPayload}"/>
/// <c>{ Payload, Outbox, Idempotency }</c>, so a state change and its outbound
/// effect commit atomically in one write. Command Handlers never touch
/// the <see cref="GrainEnvelope{TPayload}.Idempotency"/> slot (a Command
/// is a direct grain call, so there is deliberately no deduplication —
/// dedup is for at-least-once stream delivery, which Commands never use).
/// All outbox plumbing — drain algorithm, lazy
/// reminder, drain-on-activation — lives on the composed
/// <see cref="OutboxHost{TPayload}"/> field; the grain itself is a thin Orleans
/// lifecycle shell that forwards <c>OnActivateAsync</c> and
/// <c>ReceiveReminder</c>.
/// <para>
/// The consumer mutates <see cref="State"/> — its own <typeparamref name="TState"/>
/// POCO — and never hand-persists fields. The consumer writes a <c>partial</c>
/// grain with one strongly typed <c>HandleAsync(TCommand)</c> per command; the
/// source generator emits the matching <see cref="DispatchAsync"/> override
/// that type-switches to those overloads, calling
/// <see cref="ValidateAndHandleAsync{TCommand}"/> per arm.
/// </para>
/// </summary>
[StorageProvider(ProviderName = "edict-state")]
public abstract class EdictCommandHandler<TState>
    : Grain<GrainEnvelope<TState>>, IEdictCommandHandler, IEdictScheduleFireable, IRemindable
    where TState : IEdictPersistedState, new()
{
    OutboxHost<TState>? _host;
    ScheduleHost<TState>? _scheduleHost;
    AuditHost<TState>? _auditHost;
    Serializer? _serializer;
    EdictCommandHandlerScheduleOptions? _scheduleOptions;
    IDeadLetterPromoter? _deadLetterPromoter;
    bool? _auditEnabled;
    IGrainTimer? _auditDrainKick;
    internal List<EdictEvent>? _raisedEvents;
    internal TimeProvider? _timeProvider;

    OutboxHost<TState> Host => _host ??= BuildHost();

    ScheduleHost<TState> ScheduleHost => _scheduleHost ??= BuildScheduleHost();

    AuditHost<TState> AuditHost => _auditHost ??= BuildAuditHost();

    // Auditing is on for this silo when the marker is present (WithAudit + a
    // resolver). Resolved once; the common non-audited silo pays one lookup.
    bool AuditEnabled => _auditEnabled ??= ServiceProvider.GetService<EdictAuditEnabledMarker>() is not null;

    Serializer Serializer => _serializer ??= ServiceProvider.GetRequiredService<Serializer>();

    EdictCommandHandlerScheduleOptions ScheduleOptions =>
        _scheduleOptions ??= ServiceProvider.GetService<IOptions<EdictCommandHandlerScheduleOptions>>()?.Value
            ?? new EdictCommandHandlerScheduleOptions();

    IDeadLetterPromoter DeadLetterPromoter =>
        _deadLetterPromoter ??= ServiceProvider.GetRequiredService<IDeadLetterPromoter>();

    /// <summary>
    /// The framework-owned durable aggregate state. The consumer mutates this
    /// inside <c>HandleAsync</c>; it is the payload slot of the persisted envelope,
    /// committed atomically with the Outbox.
    /// </summary>
    protected new TState State => base.State.Payload;

    /// <summary>
    /// Test-only probe over the framework-owned Outbox slice. Internal so the
    /// Edict probe grains (<c>CounterAggregate</c>) can assert pending-entry
    /// counts; not part of the consumer surface.
    /// </summary>
    internal OutboxSlice OutboxStateForProbe => base.State.Outbox;

    /// <summary>
    /// Test-only probe over the framework-owned audit chain. Internal so the
    /// Edict probe grains can assert pending-record counts; not consumer surface.
    /// </summary>
    internal AuditChain? AuditStateForProbe => base.State.Audit;

    /// <inheritdoc />
    public abstract Task<EdictCommandResult> DispatchAsync(EdictCommand command);

    /// <summary>
    /// Drains anything left from a crash before the grain serves traffic
    /// (drain-on-activation). Steady state has nothing pending so
    /// this is a cheap check.
    /// </summary>
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await Host.OnActivateAsync();
        if (base.State.Schedule.Active.Count > 0)
        {
            await ScheduleHost.OnActivateAsync();
        }
        if (AuditEnabled && base.State.Audit is { Pending.Count: > 0 })
        {
            await AuditHost.OnActivateAsync();
        }
    }

    /// <summary>
    /// Removes this grain's entry from the silo-local metrics cache so a
    /// deactivated aggregate stops contributing to the per-type
    /// <c>edict.outbox.pending.count</c> / <c>edict.outbox.oldest_entry.age</c>
    /// gauges.
    /// </summary>
    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        await Host.OnDeactivateAsync();
        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    /// <inheritdoc />
    public Task ReceiveReminder(string reminderName, TickStatus status) =>
        reminderName switch
        {
            ScheduleHost<TState>.ScheduleReminderName => ScheduleHost.ReceiveReminderAsync(),
            AuditHost<TState>.DrainReminderName => AuditHost.ReceiveReminderAsync(),
            _ => Host.ReceiveReminderAsync(),
        };

    /// <inheritdoc />
    public Task FireDueSchedulesAsync() => ScheduleHost.FireDueAsync();

    /// <inheritdoc />
    public Task<DateTimeOffset?> PeekSoonestScheduleDueAsync() =>
        Task.FromResult(ScheduleHost.SoonestDueAt);

    /// <inheritdoc />
    public Task FireDueScheduleTimeoutsAsync() => ScheduleHost.FireTimeoutsAsync();

    /// <inheritdoc />
    public Task<DateTimeOffset?> PeekSoonestScheduleTimeoutAsync() =>
        Task.FromResult(ScheduleHost.SoonestTimeoutAt);

    /// <summary>
    /// Starts a recurring schedule from inside <c>HandleAsync</c>. The
    /// <paramref name="message"/> is persisted (data plus its <c>[Alias]</c>),
    /// never a delegate; each fire deserializes it and routes it back through this
    /// grain's <c>HandleAsync(TMessage) : Task&lt;EdictScheduleResult&gt;</c>,
    /// re-entering the full handler lifecycle. The first fire is at
    /// <c>+every</c>; the handler answers only <see cref="Continue"/> or
    /// <see cref="Complete"/>. The staged schedule is durable because the
    /// enclosing command commits the whole envelope; the timer and Reminder arm
    /// on that commit.
    /// <para>
    /// <paramref name="timeout"/> caps the schedule's lifetime, armed once here and
    /// never reset by a fire. An explicit positive <see cref="TimeSpan"/> wins;
    /// <see cref="EdictSchedule.Unbounded"/> opts out of any cap; omitting it
    /// inherits <see cref="EdictCommandHandlerScheduleOptions.DefaultTimeout"/>
    /// (7 days by default, or uncapped when that is null or
    /// <see cref="EdictSchedule.Unbounded"/>). On cap the schedule runs
    /// <c>OnScheduleTimeoutAsync(TMessage)</c> if one is written, else dead-letters.
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

    // Resolves the effective timeout duration for a schedule: an explicit timeout:
    // wins, EdictSchedule.Unbounded (at the call site or as the silo default) means
    // uncapped (null), and omitting timeout: inherits the silo default. The result
    // is the cap as a duration from now, or null when the schedule is uncapped.
    TimeSpan? ResolveScheduleCap(TimeSpan? timeout)
    {
        if (timeout is { } explicitTimeout)
        {
            return explicitTimeout == EdictSchedule.Unbounded ? null : explicitTimeout;
        }

        return ScheduleOptions.DefaultTimeout is { } siloDefault && siloDefault != EdictSchedule.Unbounded
            ? siloDefault
            : null;
    }

    /// <summary>Fire-handler result: fire again on the declared cadence.</summary>
    protected static Task<EdictScheduleResult> Continue() =>
        Task.FromResult<EdictScheduleResult>(new EdictScheduleResult.Continue());

    /// <summary>Fire-handler result: stop the schedule; no further fires.</summary>
    protected static Task<EdictScheduleResult> Complete() =>
        Task.FromResult<EdictScheduleResult>(new EdictScheduleResult.Complete());

    /// <summary>
    /// Generator override target: type-switches a fired message to the consumer's
    /// matching <c>HandleAsync(TMessage)</c>. The base default is unreachable — a
    /// schedule can only be armed for a message this grain dispatches.
    /// </summary>
    protected virtual Task<EdictScheduleResult> DispatchScheduleFireAsync(EdictScheduleMessage message) =>
        throw new EdictUnroutableScheduleMessageException(message.GetType());

    /// <summary>
    /// Generator override target: type-switches a timed-out message to the
    /// consumer's matching <c>OnScheduleTimeoutAsync(TMessage)</c> compensation
    /// hook, returning <c>true</c> when one ran. The base default returns
    /// <c>false</c> for every message — a schedule whose author wrote no hook
    /// dead-letters its fired cap rather than throwing.
    /// </summary>
    protected virtual Task<bool> DispatchScheduleTimeoutAsync(EdictScheduleMessage message) =>
        Task.FromResult(false);

    // The dispatch delegate ScheduleHost calls per due entry — the hand-written
    // schedule fire lifecycle, parallel to ValidateAndHandleAsync. Deserialize,
    // run the fire handler through the generated dispatch, apply the outcome to
    // the slice, and commit atomically with state and outbox. A handler throw
    // discards the turn (buffered events dropped, partial State mutation rolled
    // back to the last durable snapshot) but re-times the schedule forward so a
    // failing fire retries on the next cadence rather than spinning the timer;
    // slice 1 has no dead-letter path for a stuck schedule.
    async Task FireScheduleEntryAsync(ScheduleEntry entry, DateTimeOffset now)
    {
        var message = Serializer.Deserialize<EdictScheduleMessage>(entry.MessagePayload);

        // The fire is its own grain turn: a new trace root linking back to the
        // command that armed the schedule. Capturing it as the current context makes
        // any event the fire raises nest under it as parent-child within the turn.
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
            DiscardRaisedEvents();
            await ReadStateAsync();
            base.State.Schedule = base.State.Schedule.Continue(entry.ScheduleId, now);
            await CommitAndDrainRaisedEventsAsync();
            return;
        }

        base.State.Schedule = result is EdictScheduleResult.Complete
            ? base.State.Schedule.Complete(entry.ScheduleId)
            : base.State.Schedule.Continue(entry.ScheduleId, now);

        // A fire is a fresh causal root (new correlation) but stays attributed to the
        // arming principal and tenant carried durably in the entry, so a recurring
        // job's events are still ascribed to whoever armed it, inside that tenant
        // wall, across reactivation.
        await CommitAndDrainRaisedEventsAsync(Guid.NewGuid(), entry.ArmPrincipal, entry.ArmTenant);
    }

    // The dispatch delegate ScheduleHost calls per timed-out entry. A timeout is
    // terminal: it removes the schedule in the same commit as its outcome. If the
    // consumer wrote an OnScheduleTimeoutAsync hook, it runs as compensation
    // (mutating State / raising events, which commit atomically with the removal);
    // otherwise the cap dead-letters through DeadLetterPromoter with a no-throw
    // synthetic row. A hook that throws is rolled back to the durable snapshot and
    // dead-lettered, so a failing compensation cannot leave the cap re-firing
    // forever.
    async Task FireScheduleTimeoutEntryAsync(ScheduleEntry entry, DateTimeOffset now)
    {
        var message = Serializer.Deserialize<EdictScheduleMessage>(entry.MessagePayload);

        bool handled;
        try
        {
            handled = await DispatchScheduleTimeoutAsync(message);
        }
        catch
        {
            DiscardRaisedEvents();
            await ReadStateAsync();
            handled = false;
        }

        if (handled)
        {
            base.State.Schedule = base.State.Schedule.Complete(entry.ScheduleId);
            await CommitAndDrainRaisedEventsAsync();
            return;
        }

        var (traceId, spanId, traceState) = ActivityExtensions.ReadRequestContext();
        var traceParent = traceId is not null && spanId is not null
            ? ActivityExtensions.BuildTraceParent(traceId, spanId)
            : null;

        var deadLetter = DeadLetterPromoter.PromoteScheduleTimeout(
            scheduleMessageType: message.GetType().FullName ?? message.GetType().Name,
            sourceGrainKey: this.GetPrimaryKeyString(),
            sourceGrainType: GetType().FullName ?? GetType().Name,
            traceParent: traceParent,
            traceState: traceState,
            now: now);

        base.State.Schedule = base.State.Schedule.Complete(entry.ScheduleId);
        await Host.EnqueueAndDrainAsync([deadLetter]);
    }

    /// <summary>
    /// Buffers an event to be staged onto the Outbox when the current command
    /// completes — published on both <c>Accepted</c> and <c>Rejected</c>, since a
    /// <see cref="Raise"/> call is the consumer's explicit intent to publish.
    /// Discarded only when the handler throws.
    /// Stamped with <c>OccurredAt</c> at this call (via the framework's
    /// <see cref="TimeProvider"/>) so the timestamp reflects the moment the
    /// consumer's handler decided to publish and is preserved across any
    /// subsequent outbox delay. <c>EventId</c> is stamped once as the event
    /// enters the outbox; trace context is stamped per publish at drain.
    /// </summary>
    protected void Raise(EdictEvent theEvent)
    {
        ArgumentNullException.ThrowIfNull(theEvent);
        var time = _timeProvider ??= ServiceProvider.GetRequiredService<TimeProvider>();
        (_raisedEvents ??= []).Add(theEvent with { OccurredAt = time.GetUtcNow() });
    }

    /// <summary>
    /// Generator-only fast path called by the per-type Raise interceptor stubs.
    /// Identical semantics to <see cref="Raise"/> on the typed
    /// argument — the win is a monomorphic typed call site so the JIT can
    /// devirtualize the record-<c>with</c> clone. Not a stable public API; the
    /// interceptor emitter is the only caller.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void RaiseFast<TEvent>(TEvent theEvent) where TEvent : EdictEvent
    {
        ArgumentNullException.ThrowIfNull(theEvent);
        var time = _timeProvider ??= ServiceProvider.GetRequiredService<TimeProvider>();
        (_raisedEvents ??= []).Add(theEvent with { OccurredAt = time.GetUtcNow() });
    }

    /// <summary>
    /// Commit primitive for a path with no upstream message — a schedule or timer
    /// fire, or a <c>RegisterGrainTimer</c> escape-hatch callback. Mints a fresh
    /// correlation for any raised events, the correct new-causal-root semantics:
    /// a time-trigger continues no earlier conversation.
    /// </summary>
    protected Task CommitAndDrainRaisedEventsAsync() => CommitAndDrainRaisedEventsAsync(Guid.NewGuid());

    /// <summary>
    /// The shared commit primitive for a completing handler. When events were
    /// raised, stages them as <see cref="OutboxEffectKind.PublishEvent"/> entries
    /// (each stamped with <paramref name="correlationId"/>, inherited from the
    /// handling command), commits <c>{ State, Outbox }</c> in one write, then
    /// awaits the inline FIFO drain. When none were raised, commits
    /// <c>{ State, Outbox }</c> alone (no enqueue, no drain) so a handler that
    /// mutated <c>State</c> and raised nothing keeps the mutation across
    /// deactivation. A post-commit publish failure does not roll back and does
    /// not surface — the Reminder retries.
    /// </summary>
    protected async Task CommitAndDrainRaisedEventsAsync(Guid correlationId, EdictPrincipal? principal = null, EdictTenantId? tenant = null)
    {
        var events = _raisedEvents;
        _raisedEvents = null;

        if (events is null || events.Count == 0)
        {
            await Host.WriteStateOnlyAsync();
        }
        else
        {
            // Capture the live command trace so the publish span nests under it as
            // parent-child even when a crash-recovery drain runs much later.
            var (traceId, spanId, traceState) = ActivityExtensions.ReadRequestContext();
            var traceParent = traceId is not null && spanId is not null
                ? ActivityExtensions.BuildTraceParent(traceId, spanId)
                : null;

            await Host.EnqueueRaisedEventsAndDrainAsync(
                events, traceParent, traceState, correlationId, principal, tenant,
                AuditEnabled ? StageEventAuditRecords : null);
        }

        // Any audit record staged into the write above — the command's C1 record,
        // plus an E1 record per raised event — drains off this turn. Arm the durable
        // reminder at stage time, right after that write, so the record has a puller
        // even if the grain deactivates before the kicked timer runs; the timer is the
        // fast path and a successful drain unregisters the reminder. Both steps cover
        // a schedule or timer fire that raises an audited event too, and the common
        // non-audited silo skips them entirely. Guarded on pending so a fire that
        // raises nothing audited never arms a reminder over no work.
        if (AuditEnabled && base.State.Audit is { Pending.Count: > 0 })
        {
            await AuditHost.ArmDrainReminderAsync();
            KickAuditDrain();
        }
    }

    /// <summary>Discards all buffered events. Called when the handler throws.</summary>
    protected void DiscardRaisedEvents() => _raisedEvents = null;

    /// <summary>
    /// Owns the command lifecycle the generated dispatch spine delegates to per
    /// arm: validate, then handle, then commit. A Command Validator rejection
    /// short-circuits before <paramref name="handle"/> runs and writes nothing —
    /// the handler never executed, so there is no <c>State</c> mutation or event to
    /// commit. Otherwise the handler runs and its <c>State</c> persists on
    /// completion, on both <see cref="EdictCommandResult.Accepted"/> and
    /// <see cref="EdictCommandResult.Rejected"/> and independent of whether an
    /// event was raised; raised events publish whenever <see cref="Raise"/> was
    /// called, regardless of the result. A handler throw discards the turn: the
    /// buffered events are dropped and the partial <c>State</c> mutation is rolled
    /// back by reloading the last durable snapshot within the same turn (no write
    /// was attempted, so that snapshot is known-good), then the exception
    /// propagates.
    /// </summary>
    protected async Task<EdictCommandResult> ValidateAndHandleAsync<TCommand>(
        TCommand command,
        Func<Task<EdictCommandResult>> handle)
        where TCommand : EdictCommand
    {
        var grainTypeName = GetType().FullName ?? GetType().Name;

        // The silo-side handle span. On the awaited API path it restores the Web
        // edict.command context as its parent so the callee nests as a child. On a
        // saga's fire-and-forget outbox dispatch it is its own grain turn: a new root
        // linking back to the edict.command.send producer. Either way it wraps
        // validate -> handle -> raise and carries the same route-key and
        // [EdictTelemeterized] tags as the producing span so a tag query resolves on
        // both sides.
        var producerContext = ActivityExtensions.RestoreFromRequestContext();
        var isCrossTurnDispatch = ActivityExtensions.IsCrossTurnLink();
        using var activity = isCrossTurnDispatch
            ? EdictDiagnostics.ActivitySource.StartEdictCommandHandleLinked(typeof(TCommand).Name, producerContext)
            : EdictDiagnostics.ActivitySource.StartEdictCommandHandle(typeof(TCommand).Name, producerContext);

        if (isCrossTurnDispatch)
        {
            // The dispatched command runs in its own trace, linked to the saga's send
            // span. Re-anchor RequestContext to this handle span so an event the
            // handler raises publishes within this turn instead of back under the
            // saga's send span, and drop the cross-turn marker so a command this
            // handler itself dispatches is treated normally.
            ActivityExtensions.ClearCrossTurnLink();
            activity?.CaptureToRequestContext();
        }

        if (activity is not null)
        {
            activity.SetEdictCommandTags(this.GetPrimaryKeyString());
            if (ServiceProvider.GetService<CommandRouteResolver>() is { } resolver
                && resolver.TryGetRoute(command, out var route))
            {
                route!.TagWriter?.Invoke(command, activity);
            }
        }

        // Seed the turn's principal and tenant off the inbound command so a schedule
        // the handler arms inherits the originating actor and tenant wall through the
        // relay, the same slot that already persists this command's trace context for
        // the fire to link back to.
        OriginIdentity.Seed(command.Principal, command.Tenant);

        var validator = ServiceProvider.GetService<IValidator<TCommand>>();

        if (validator is not null)
        {
            var context = new ValidationContext<TCommand>(command);
            var state = GetValidationState();
            if (state is not null)
            {
                context.RootContextData[SemanticConventions.Validation.GrainStateKey] = state;
            }

            var validation = await validator.ValidateAsync(context);
            if (!validation.IsValid)
            {
                var rejected = new EdictCommandResult.Rejected(
                    validation.Errors
                        .Select(static e => new EdictRejectionReason(
                            e.ErrorCode ?? "validation_error",
                            e.ErrorMessage))
                        .ToArray());

                // A validator rejection never ran the handler, so it has no state
                // mutation or event to commit and normally writes nothing. When
                // auditing is on the rejection is still a decision the regulator
                // asks about ("who tried, and why was it denied?"), so capture it
                // and commit that one record before acknowledging the rejection.
                if (AuditEnabled)
                {
                    StageAuditRecord(command, rejected);
                    await Host.WriteStateOnlyAsync();
                    await AuditHost.ArmDrainReminderAsync();
                    KickAuditDrain();
                }

                return rejected;
            }
        }

        EdictCommandResult result;
        try
        {
            result = await CommandHandleMetrics.RunAndRecordAsync<TCommand>(handle, grainTypeName);
        }
        catch
        {
            // Roll the partial mutation back to the last durable snapshot so a
            // dirty activation never serves the next command; the buffered events
            // are dropped with it. No write was attempted, so the snapshot is good.
            DiscardRaisedEvents();
            await ReadStateAsync();
            throw;
        }

        // Stage the decision before the commit so the audit record rides the same
        // grain-state write as the action — present before the result is
        // acknowledged. The drain to the store happens off this turn, below.
        if (AuditEnabled)
        {
            StageAuditRecord(command, result);
        }

        await CommitAndDrainRaisedEventsAsync(command.CorrelationId, command.Principal, command.Tenant);

        // A handler that called Schedule(...) staged an entry that the commit
        // above just made durable; arm its timer and Reminder now. Skipped
        // entirely for the common non-scheduling handler (no host built, no
        // durable schedules), so the cost there is one Count check.
        if (_scheduleHost is not null || base.State.Schedule.Active.Count > 0)
        {
            await ScheduleHost.ReconcileAsync();
        }

        // Echo the command's chain-stable correlation back as the read-your-writes
        // cursor. Stamped here, the single chokepoint every dispatch funnels
        // through, so a consumer handler keeps returning a bare Accepted and never
        // threads the correlation by hand.
        return result is EdictCommandResult.Accepted accepted
            ? accepted with { Cursor = new EdictCursor(command.CorrelationId) }
            : result;
    }

    /// <summary>
    /// Override to expose the grain's current state to validators via
    /// <c>ValidationContext.RootContextData[<see cref="SemanticConventions.Validation.GrainStateKey"/>]</c>.
    /// The default returns <c>null</c> (no state injected).
    /// </summary>
    protected virtual object? GetValidationState() => null;

    // Builds the C1 audit record for a command decision and stages it onto the
    // chain in grain state. The hash folds in the running chain head, so the
    // record both attributes the decision and seals it against tampering; the
    // serialized record waits on the chain for the off-hot-path drain. The body
    // is captured as a hash plus a reference id only, so the chain itself stays
    // personal-data-free.
    void StageAuditRecord<TCommand>(TCommand command, EdictCommandResult result)
        where TCommand : EdictCommand
    {
        var outcome = result is EdictCommandResult.Accepted
            ? EdictAuditOutcome.Accepted
            : EdictAuditOutcome.Rejected;
        var reasons = result is EdictCommandResult.Rejected rejected
            ? rejected.Reasons
            : [];

        var time = _timeProvider ??= ServiceProvider.GetRequiredService<TimeProvider>();
        var recordId = Guid.NewGuid();
        var body = Serializer.SerializeToArray<EdictCommand>(command);
        var content = new EdictAuditRecord
        {
            RecordId = recordId,
            Kind = EdictAuditKind.Command,
            Outcome = outcome,
            RejectionReasons = reasons,
            Principal = command.Principal,
            Tenant = command.Tenant,
            CorrelationId = command.CorrelationId,
            EntityType = GetType().FullName ?? GetType().Name,
            EntityKey = this.GetPrimaryKeyString(),
            MessageType = command.GetType().FullName ?? command.GetType().Name,
            OccurredAt = time.GetUtcNow(),
            PayloadHash = SHA256.HashData(body),
        };

        AppendToChain(content, body);

        AuditMetrics.RecordsCaptured.Add(1,
            new KeyValuePair<string, object?>(
                SemanticConventions.Audit.Tags.Kind, SemanticConventions.Audit.Tags.KindValues.Command),
            new KeyValuePair<string, object?>(
                SemanticConventions.Audit.Tags.Outcome,
                outcome == EdictAuditOutcome.Accepted
                    ? SemanticConventions.Audit.Tags.OutcomeValues.Accepted
                    : SemanticConventions.Audit.Tags.OutcomeValues.Rejected));
    }

    // Stages one E1 audit record per raised event onto the chain, in capture
    // order, after the command's own C1 record. Invoked from the outbox enqueue
    // chokepoint with the events already carrying their minted EventId and the
    // inherited correlation and principal, so each record attributes the event to
    // the originating actor and seals against the running chain head before the
    // commit that makes both the events and the records durable in one write. The
    // RecordId is the EventId, so the record and the event it attests to share one
    // identity and a re-drain dedups on it. OccurredAt is the event's intent-time
    // stamped at Raise, not the enqueue moment.
    void StageEventAuditRecords(IReadOnlyList<EdictEvent> events)
    {
        var entityType = GetType().FullName ?? GetType().Name;
        var entityKey = this.GetPrimaryKeyString();

        foreach (var raisedEvent in events)
        {
            var body = Serializer.SerializeToArray(raisedEvent);
            var content = new EdictAuditRecord
            {
                RecordId = raisedEvent.EventId,
                Kind = EdictAuditKind.Event,
                Principal = raisedEvent.Principal,
                Tenant = raisedEvent.Tenant,
                CorrelationId = raisedEvent.CorrelationId,
                EntityType = entityType,
                EntityKey = entityKey,
                MessageType = raisedEvent.GetType().FullName ?? raisedEvent.GetType().Name,
                OccurredAt = raisedEvent.OccurredAt,
                PayloadHash = SHA256.HashData(body),
            };

            AppendToChain(content, body);

            AuditMetrics.RecordsCaptured.Add(1,
                new KeyValuePair<string, object?>(
                    SemanticConventions.Audit.Tags.Kind, SemanticConventions.Audit.Tags.KindValues.Event));
        }
    }

    // Seals a content-filled record against the running chain head and stages it,
    // with the captured body, onto the pending tail in grain state. Shared by C1
    // and E1 capture so the chain advance and the serialized-entry shape stay
    // identical for both. The body is the exact bytes content.PayloadHash was
    // computed over, so a re-hash of the drained body recomputes the sealed hash.
    void AppendToChain(EdictAuditRecord content, byte[] body)
    {
        var chain = base.State.Audit ?? new AuditChain();
        var sealedRecord = AuditRecordBuilder.Seal(content, chain.Head, chain.NextSequence);
        var entry = new AuditChainEntry
        {
            RecordId = sealedRecord.RecordId,
            Record = Serializer.SerializeToArray(sealedRecord),
            Payload = body,
        };
        base.State.Audit = chain.Append(sealedRecord.RecordHash, entry);
    }

    // Drains the staged records off the command turn so the caller never waits on
    // the remote store write, while staying on the grain scheduler so it
    // serialises with the next turn. One pending kick at a time; activation and
    // the reminder backstop a kick that never fires.
    void KickAuditDrain()
    {
        _auditDrainKick?.Dispose();
        _auditDrainKick = this.RegisterGrainTimer(
            async _ => await AuditHost.DrainAsync(),
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.Zero,
                Period = Timeout.InfiniteTimeSpan,
                KeepAlive = true,
            });
    }

    OutboxHost<TState> BuildHost() =>
        new(
            new GrainPersistentStateAdapter<GrainEnvelope<TState>>(
                get: () => base.State,
                set: v => base.State = v,
                writeState: WriteStateAsync),
            this.GetStreamProvider("edict"),
            new GrainReminderRegistrar(this),
            ServiceProvider.GetServices<IOutboxEffectExecutor>(),
            ServiceProvider.GetRequiredService<IOptions<EdictOptions>>().Value,
            ServiceProvider.GetRequiredService<TimeProvider>(),
            ServiceProvider.GetRequiredService<IDeadLetterPromoter>(),
            grainKey: this.GetPrimaryKeyString(),
            grainTypeName: GetType().FullName ?? GetType().Name,
            claimCheckPolicy: ResolveClaimCheckPolicy(ServiceProvider),
            metricsCache: ServiceProvider.GetService<IEdictMetricsCache>(),
            requestDeactivation: DeactivateOnIdle);

    ScheduleHost<TState> BuildScheduleHost() =>
        new(
            new GrainPersistentStateAdapter<GrainEnvelope<TState>>(
                get: () => base.State,
                set: v => base.State = v,
                writeState: WriteStateAsync),
            new GrainReminderRegistrar(this),
            new GrainScheduleTimer(this),
            ServiceProvider.GetRequiredService<TimeProvider>(),
            ServiceProvider.GetRequiredService<IOptions<EdictOptions>>().Value,
            FireScheduleEntryAsync,
            FireScheduleTimeoutEntryAsync);

    AuditHost<TState> BuildAuditHost() =>
        new(
            new GrainPersistentStateAdapter<GrainEnvelope<TState>>(
                get: () => base.State,
                set: v => base.State = v,
                writeState: WriteStateAsync),
            new GrainReminderRegistrar(this),
            ServiceProvider.GetRequiredService<IEdictAuditStore>(),
            ServiceProvider.GetRequiredService<IEdictAuditPayloadStore>(),
            Serializer,
            ServiceProvider.GetRequiredService<IOptions<EdictOptions>>().Value.OutboxDrainReminderPeriod,
            GetType().FullName ?? GetType().Name);

    static ClaimCheckPolicy ResolveClaimCheckPolicy(IServiceProvider serviceProvider) =>
        // AddEdictOutbox registers the default policy; pre-existing test
        // fixtures that hand-wire individual services pre-date that
        // registration. Fall back to a never-trip policy so consumer code
        // works either way.
        serviceProvider.GetService<ClaimCheckPolicy>()
        ?? new ClaimCheckPolicy(serviceProvider.GetRequiredService<Serializer>(), int.MaxValue, null, serviceProvider.GetRequiredService<IEventStreamAccessors>());
}

/// <summary>
/// Stateless-handler convenience shim over <see cref="EdictCommandHandler{TState}"/>
/// closed on <see cref="EdictUnit"/>, so a handler that needs no aggregate state
/// derives from a bare <c>EdictCommandHandler</c> without writing
/// <c>&lt;EdictUnit&gt;</c> across hundreds of handlers. The Outbox slice still
/// exists on the envelope; only the payload is empty.
/// </summary>
public abstract class EdictCommandHandler : EdictCommandHandler<EdictUnit>;
