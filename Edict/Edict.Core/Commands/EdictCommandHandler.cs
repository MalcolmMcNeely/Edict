using Edict.Contracts;
using Edict.Contracts.Commands;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core;
using Edict.Core.Administration;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Telemetry;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Commands;

/// <summary>
/// Base for an aggregate grain. The framework owns durable aggregate state: the
/// persisted document is the single-write <see cref="GrainEnvelope{TState}"/>
/// <c>{ TState, Outbox, DeadLetter }</c>, so a state change and its outbound
/// effect commit atomically in one write (ADR 0018, amends ADR 0004). The
/// consumer mutates <see cref="State"/> — its own <typeparamref name="TState"/>
/// POCO — and never hand-persists fields. A Command is a direct grain call, so
/// there is deliberately no deduplication here (dedup is for at-least-once
/// stream delivery, which Commands never use — ADR 0004). The consumer writes a
/// <c>partial</c> grain with one strongly typed <c>Handle(TCommand)</c> per
/// command; the source generator emits the matching <see cref="DispatchAsync"/>
/// override that type-switches to those overloads, calling
/// <see cref="ValidateAndHandleAsync{TCommand}"/> per arm.
/// <para>
/// After <c>Accepted</c>, raised events become <see cref="OutboxEffectKind.PublishEvent"/>
/// entries staged onto the Outbox and committed in the same write as
/// <typeparamref name="TState"/>; the <see cref="OutboxDrainEngine"/> then
/// publishes them via the inline FIFO drain. A lazy Orleans Reminder is the
/// crash-recovery net — registered only while the Outbox is non-empty,
/// unregistered on full drain, plus drain-on-activation — so steady state holds
/// zero reminders (ADR 0018).
/// </para>
/// </summary>
[StorageProvider(ProviderName = "edict-state")]
public abstract class EdictCommandHandler<TState>
    : Grain<GrainEnvelope<TState>>, IEdictCommandHandler, IRemindable, IOutboxHost, IEdictDeadLetterAdmin
    where TState : new()
{
    const string DrainReminderName = "edict-outbox-drain";

    List<EdictEvent>? _raisedEvents;
    bool _drainReminderRegistered;

    /// <summary>
    /// The framework-owned durable aggregate state. The consumer mutates this
    /// inside <c>Handle</c>; it is the payload slot of the persisted envelope,
    /// committed atomically with the Outbox (ADR 0018).
    /// </summary>
    protected new TState State => base.State.Payload;

    OutboxDrainEngine Engine => ServiceProvider.GetRequiredService<OutboxDrainEngine>();

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
        if (base.State.Outbox.Pending.Count > 0)
        {
            await Engine.DrainAsync(this);
        }
    }

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
        var entries = BuildPendingEntries();
        _raisedEvents = null;
        await Engine.EnqueueAndDrainAsync(this, entries);
    }

    IReadOnlyList<OutboxEntry> BuildPendingEntries()
    {
        if (_raisedEvents is null || _raisedEvents.Count == 0)
        {
            return [];
        }

        var serializer = ServiceProvider.GetRequiredService<Serializer>();

        // Capture the live command trace so the publish span nests under it as
        // parent-child even when a crash-recovery drain runs much later (ADR 0003).
        var (traceId, spanId, traceState) = ActivityExtensions.ReadRequestContext();
        var traceParent = traceId is not null && spanId is not null
            ? ActivityExtensions.BuildTraceParent(traceId, spanId)
            : null;

        var entries = new List<OutboxEntry>(_raisedEvents.Count);
        foreach (var evt in _raisedEvents)
        {
            entries.Add(new OutboxEntry
            {
                EntryId = Guid.NewGuid(),
                Kind = OutboxEffectKind.PublishEvent,
                Payload = serializer.SerializeToArray<EdictEvent>(evt),
                TraceParent = traceParent,
                TraceState = traceState,
            });
        }

        return entries;
    }

    /// <summary>Discards all buffered events. Called on <c>Rejected</c> or handler throw.</summary>
    protected void DiscardRaisedEvents() => _raisedEvents = null;

    /// <summary>
    /// Operator recovery (ADR 0019): atomically moves the dead-lettered entry
    /// back to the Outbox tail with <c>AttemptCount</c> reset, then drains.
    /// This is the only mutation path for a dead-lettered entry.
    /// </summary>
    async Task IEdictDeadLetterAdmin.RedriveAsync(Guid entryId)
    {
        var clock = ServiceProvider.GetRequiredService<TimeProvider>();
        base.State.Outbox = base.State.Outbox.Redrive(entryId, clock.GetUtcNow());
        await WriteStateAsync();
        await Engine.DrainAsync(this);
    }

    /// <inheritdoc />
    Task<IReadOnlyList<EdictDeadLetterEntry>> IEdictDeadLetterAdmin.ListDeadLetterAsync() =>
        Task.FromResult(DeadLetterProjection.From(base.State.Outbox));

    /// <inheritdoc />
    public Task ReceiveReminder(string reminderName, TickStatus status)
    {
        // A tick proves a reminder exists; record that so the post-drain
        // reconcile authoritatively unregisters it once the Outbox is empty.
        _drainReminderRegistered = true;
        return Engine.DrainAsync(this);
    }

    OutboxSlice IOutboxHost.Outbox
    {
        get => base.State.Outbox;
        set => base.State.Outbox = value;
    }

    IStreamProvider IOutboxHost.StreamProvider => this.GetStreamProvider("edict");

    Task IOutboxHost.CommitAsync() => WriteStateAsync();

    async Task IOutboxHost.RegisterDrainReminderAsync()
    {
        await this.RegisterOrUpdateReminder(
            DrainReminderName, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        _drainReminderRegistered = true;
    }

    async Task IOutboxHost.UnregisterDrainReminderAsync()
    {
        if (!_drainReminderRegistered)
        {
            return; // never registered — keep the happy path off the reminder subsystem
        }

        var reminder = await this.GetReminder(DrainReminderName);
        if (reminder is not null)
        {
            await this.UnregisterReminder(reminder);
        }

        _drainReminderRegistered = false;
    }

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
        // Block-intake (ADR 0019): a saturated DeadLetter slice surfaces an
        // infrastructure fault (thrown, never a business Rejected) so the
        // effect is never silently dropped until an operator redrives.
        var outboxOptions = ServiceProvider.GetRequiredService<EdictOutboxOptions>();
        if (base.State.Outbox.IsIntakeBlocked(outboxOptions.DeadLetterCap))
        {
            throw new EdictOutboxSaturatedException();
        }

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
}

/// <summary>
/// Stateless-handler convenience shim over <see cref="EdictCommandHandler{TState}"/>
/// closed on <see cref="EdictUnit"/>, so a handler that needs no aggregate state
/// derives from a bare <c>EdictCommandHandler</c> without writing
/// <c>&lt;EdictUnit&gt;</c> across hundreds of handlers. The Outbox/DeadLetter
/// slice still exists on the envelope; only the payload is empty.
/// </summary>
public abstract class EdictCommandHandler : EdictCommandHandler<EdictUnit>;
