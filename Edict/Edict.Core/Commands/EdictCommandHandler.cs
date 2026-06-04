using System.ComponentModel;

using Edict.Contracts;
using Edict.Contracts.Commands;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.ClaimCheck;
using Edict.Core.DeadLetter;
using Edict.Core.Metrics;
using Edict.Core.Outbox;
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
    : Grain<GrainEnvelope<TState>>, IEdictCommandHandler, IRemindable
    where TState : IEdictPersistedState, new()
{
    OutboxHost<TState>? _host;
    internal List<EdictEvent>? _raisedEvents;
    internal TimeProvider? _timeProvider;

    OutboxHost<TState> Host => _host ??= BuildHost();

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
        Host.ReceiveReminderAsync();

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
    /// The shared commit primitive for a completing handler. When events were
    /// raised, stages them as <see cref="OutboxEffectKind.PublishEvent"/> entries,
    /// commits <c>{ State, Outbox }</c> in one write, then awaits the inline FIFO
    /// drain. When none were raised, commits <c>{ State, Outbox }</c> alone (no
    /// enqueue, no drain) so a handler that mutated <c>State</c> and raised nothing
    /// keeps the mutation across deactivation. Both the dispatch lifecycle and the
    /// grain-timer escape hatch (<c>RegisterGrainTimer</c> callbacks) route through
    /// here. A post-commit publish failure does not roll back and does not surface
    /// — the Reminder retries.
    /// </summary>
    protected async Task CommitAndDrainRaisedEventsAsync()
    {
        var events = _raisedEvents;
        _raisedEvents = null;

        if (events is null || events.Count == 0)
        {
            await Host.WriteStateOnlyAsync();
            return;
        }

        // Capture the live command trace so the publish span nests under it as
        // parent-child even when a crash-recovery drain runs much later.
        var (traceId, spanId, traceState) = ActivityExtensions.ReadRequestContext();
        var traceParent = traceId is not null && spanId is not null
            ? ActivityExtensions.BuildTraceParent(traceId, spanId)
            : null;

        await Host.EnqueueRaisedEventsAndDrainAsync(events, traceParent, traceState);
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
                return new EdictCommandResult.Rejected(
                    validation.Errors
                        .Select(static e => new EdictRejectionReason(
                            e.ErrorCode ?? "validation_error",
                            e.ErrorMessage))
                        .ToArray());
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

        await CommitAndDrainRaisedEventsAsync();
        return result;
    }

    /// <summary>
    /// Override to expose the grain's current state to validators via
    /// <c>ValidationContext.RootContextData[<see cref="SemanticConventions.Validation.GrainStateKey"/>]</c>.
    /// The default returns <c>null</c> (no state injected).
    /// </summary>
    protected virtual object? GetValidationState() => null;

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
            grainKey: this.GetPrimaryKey().ToString(),
            grainTypeName: GetType().FullName ?? GetType().Name,
            claimCheckPolicy: ResolveClaimCheckPolicy(ServiceProvider),
            metricsCache: ServiceProvider.GetService<IEdictMetricsCache>(),
            requestDeactivation: DeactivateOnIdle);

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
