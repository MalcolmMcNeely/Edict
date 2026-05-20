using Edict.Contracts;
using Edict.Contracts.Commands;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Telemetry;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Edict.Core.Commands;

/// <summary>
/// Base for an aggregate grain. The framework owns durable aggregate state: the
/// persisted document is the single-write <see cref="GrainEnvelope{TPayload}"/>
/// <c>{ Payload, Outbox, Idempotency }</c>, so a state change and its outbound
/// effect commit atomically in one write (ADR 0018, amends ADR 0004). Command
/// Handlers never touch the <see cref="GrainEnvelope{TPayload}.Idempotency"/>
/// slot (a Command is a direct grain call, so there is deliberately no
/// deduplication — dedup is for at-least-once stream delivery, which Commands
/// never use — ADR 0004). All outbox plumbing — drain algorithm, lazy
/// reminder, drain-on-activation — lives on the composed
/// <see cref="OutboxHost{TPayload}"/> field; the grain itself is a thin Orleans
/// lifecycle shell that forwards <c>OnActivateAsync</c> and
/// <c>ReceiveReminder</c>.
/// <para>
/// The consumer mutates <see cref="State"/> — its own <typeparamref name="TState"/>
/// POCO — and never hand-persists fields. The consumer writes a <c>partial</c>
/// grain with one strongly typed <c>Handle(TCommand)</c> per command; the
/// source generator emits the matching <see cref="DispatchAsync"/> override
/// that type-switches to those overloads, calling
/// <see cref="ValidateAndHandleAsync{TCommand}"/> per arm.
/// </para>
/// </summary>
[StorageProvider(ProviderName = "edict-state")]
public abstract class EdictCommandHandler<TState>
    : Grain<GrainEnvelope<TState>>, IEdictCommandHandler, IRemindable
    where TState : new()
{
    OutboxHost<TState>? _host;
    List<EdictEvent>? _raisedEvents;

    OutboxHost<TState> Host => _host ??= BuildHost();

    /// <summary>
    /// The framework-owned durable aggregate state. The consumer mutates this
    /// inside <c>Handle</c>; it is the payload slot of the persisted envelope,
    /// committed atomically with the Outbox (ADR 0018).
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
    /// (drain-on-activation, ADR 0018). Steady state has nothing pending so
    /// this is a cheap check.
    /// </summary>
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await Host.OnActivateAsync();
    }

    /// <inheritdoc />
    public Task ReceiveReminder(string reminderName, TickStatus status) =>
        Host.ReceiveReminderAsync();

    /// <summary>
    /// Buffers an event to be staged onto the Outbox after the current command
    /// returns <c>Accepted</c>. Discarded on <c>Rejected</c> or handler throw.
    /// Stamped with <c>EventId</c>, <c>OccurredAt</c>, and trace context when
    /// the drain publishes it (ADR 0011).
    /// </summary>
    protected void Raise(EdictEvent theEvent)
    {
        ArgumentNullException.ThrowIfNull(theEvent);
        (_raisedEvents ??= []).Add(theEvent);
    }

    /// <summary>
    /// Stages buffered events as <see cref="OutboxEffectKind.PublishEvent"/>
    /// entries, commits <c>{ State, Outbox }</c> in one write, then awaits the
    /// inline FIFO drain. Called by the generated <c>Dispatch</c> after
    /// <c>Handle</c> returns <c>Accepted</c>. A post-commit publish failure does
    /// not roll back and does not surface — the Reminder retries (ADR 0018).
    /// </summary>
    protected async Task CommitAndDrainRaisedEventsAsync()
    {
        var events = _raisedEvents;
        _raisedEvents = null;

        if (events is null || events.Count == 0)
        {
            return;
        }

        // Capture the live command trace so the publish span nests under it as
        // parent-child even when a crash-recovery drain runs much later (ADR 0003).
        var (traceId, spanId, traceState) = ActivityExtensions.ReadRequestContext();
        var traceParent = traceId is not null && spanId is not null
            ? ActivityExtensions.BuildTraceParent(traceId, spanId)
            : null;

        await Host.EnqueueRaisedEventsAndDrainAsync(events, traceParent, traceState);
    }

    /// <summary>Discards all buffered events. Called on <c>Rejected</c> or handler throw.</summary>
    protected void DiscardRaisedEvents() => _raisedEvents = null;

    /// <summary>
    /// Resolves <see cref="IValidator{TCommand}"/> from grain DI, runs it with
    /// the current grain state in <c>ValidationContext.RootContextData</c>, and
    /// short-circuits to <see cref="EdictCommandResult.Rejected"/> on failure.
    /// Returns the result of <paramref name="handle"/> when validation passes or
    /// no validator is registered. Called from the generated <c>Dispatch</c>.
    /// </summary>
    protected async Task<EdictCommandResult> ValidateAndHandleAsync<TCommand>(
        TCommand command,
        Func<Task<EdictCommandResult>> handle)
        where TCommand : EdictCommand
    {
        var validator = ServiceProvider.GetService<IValidator<TCommand>>();

        if (validator is null)
        {
            return await handle();
        }

        var context = new ValidationContext<TCommand>(command);
        var state = GetValidationState();
        if (state is not null)
        {
            context.RootContextData[EdictValidationKeys.GrainState] = state;
        }

        var result = await validator.ValidateAsync(context);
        if (!result.IsValid)
        {
            return new EdictCommandResult.Rejected(
                result.Errors
                    .Select(static e => new EdictRejectionReason(
                        e.ErrorCode ?? "validation_error",
                        e.ErrorMessage))
                    .ToArray());
        }

        return await handle();
    }

    /// <summary>
    /// Override to expose the grain's current state to validators via
    /// <c>ValidationContext.RootContextData[<see cref="EdictValidationKeys.GrainState"/>]</c>.
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
            ServiceProvider.GetRequiredService<EdictOutboxOptions>(),
            ServiceProvider.GetRequiredService<TimeProvider>(),
            ServiceProvider.GetRequiredService<IDeadLetterPromoter>(),
            grainKey: this.GetPrimaryKey().ToString(),
            grainTypeName: GetType().FullName ?? GetType().Name,
            claimCheckPolicy: ResolveClaimCheckPolicy(ServiceProvider));

    static ClaimCheckPolicy ResolveClaimCheckPolicy(IServiceProvider sp) =>
        // AddEdictOutbox registers the default policy; pre-existing test
        // fixtures that hand-wire individual services pre-date that
        // registration. Fall back to a never-trip policy so consumer code
        // works either way.
        sp.GetService<ClaimCheckPolicy>()
        ?? new ClaimCheckPolicy(sp.GetRequiredService<Serializer>(), int.MaxValue, null);
}

/// <summary>
/// Stateless-handler convenience shim over <see cref="EdictCommandHandler{TState}"/>
/// closed on <see cref="EdictUnit"/>, so a handler that needs no aggregate state
/// derives from a bare <c>EdictCommandHandler</c> without writing
/// <c>&lt;EdictUnit&gt;</c> across hundreds of handlers. The Outbox slice still
/// exists on the envelope; only the payload is empty.
/// </summary>
public abstract class EdictCommandHandler : EdictCommandHandler<EdictUnit>;
