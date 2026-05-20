using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Core.Administration;
using Edict.Core.DeadLetter;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Outbox;

/// <summary>
/// Internal intermediate base shared by the two consumer-facing grain roots
/// (<c>EdictCommandHandler&lt;TState&gt;</c> and
/// <c>EdictIdempotencyBase&lt;TPayload&gt;</c>): the single home for the
/// <see cref="IOutboxHost"/> adapter, the <see cref="IRemindable"/> tick, the
/// <see cref="IEdictDeadLetterAdmin"/> operator surface, drain-on-activation,
/// the intake-block guard, the lazy drain-Reminder bookkeeping and the
/// <c>[StorageProvider]</c> binding. Both roots inherit this so the duplicated
/// host plumbing has one home and one persisted-document contract
/// (ADR 0017 clause (b) outer shared root; ADR 0018 unified envelope).
/// <para>
/// The <see cref="OutboxDrainEngine"/> seam itself is untouched — this is the
/// adapter that the engine drives via <see cref="IOutboxHost"/>; the engine
/// stays a plain class, the grain owns the single <c>WriteStateAsync</c>, the
/// stream provider and the Reminder.
/// </para>
/// </summary>
[StorageProvider(ProviderName = "edict-state")]
public abstract class EdictDurableConsumerBase<TPayload>
    : Grain<GrainEnvelope<TPayload>>, IOutboxHost, IRemindable, IEdictDeadLetterAdmin
    where TPayload : new()
{
    internal const string DrainReminderName = "edict-outbox-drain";

    bool _drainReminderRegistered;

    OutboxDrainEngine Engine => ServiceProvider.GetRequiredService<OutboxDrainEngine>();

    /// <summary>
    /// Drains anything left from a crash before the grain serves traffic
    /// (drain-on-activation, ADR 0018). Steady state has nothing pending so
    /// this is a cheap check.
    /// </summary>
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        if (State.Outbox.Pending.Count > 0)
        {
            await Engine.DrainAsync(this);
        }
    }

    /// <summary>
    /// Block-intake guard (ADR 0019): throws <see cref="EdictOutboxSaturatedException"/>
    /// when the DeadLetter slice is at the configured cap so a redelivered
    /// event or accepted command is never silently dropped until an operator
    /// redrives. A no-op while the cap is clear.
    /// </summary>
    protected void EnsureIntakeNotBlocked()
    {
        var options = ServiceProvider.GetRequiredService<EdictOutboxOptions>();
        if (State.Outbox.IsIntakeBlocked(options.DeadLetterCap))
        {
            throw new EdictOutboxSaturatedException();
        }
    }

    /// <inheritdoc />
    public Task ReceiveReminder(string reminderName, TickStatus status)
    {
        // A tick proves a reminder exists; record that so the post-drain
        // reconcile authoritatively unregisters it once the Outbox is empty.
        _drainReminderRegistered = true;
        return Engine.DrainAsync(this);
    }

    /// <summary>
    /// Operator recovery (ADR 0019): atomically moves the dead-lettered entry
    /// back to the Outbox tail with <c>AttemptCount</c> reset, writes state,
    /// then drains. The same one grain-state write clears the cap, so a
    /// previously blocked consumer resumes acking redelivered events.
    /// </summary>
    async Task IEdictDeadLetterAdmin.RedriveAsync(Guid entryId)
    {
        var clock = ServiceProvider.GetRequiredService<TimeProvider>();
        State.Outbox = State.Outbox.Redrive(entryId, clock.GetUtcNow());
        await WriteStateAsync();
        await Engine.DrainAsync(this);
    }

    /// <inheritdoc />
    Task<IReadOnlyList<EdictDeadLetterEntry>> IEdictDeadLetterAdmin.ListDeadLetterAsync() =>
        Task.FromResult(DeadLetterProjection.From(State.Outbox));

    OutboxSlice IOutboxHost.Outbox
    {
        get => State.Outbox;
        set => State.Outbox = value;
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
}
