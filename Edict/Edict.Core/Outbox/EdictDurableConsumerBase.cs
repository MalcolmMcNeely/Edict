using Edict.Contracts.Events;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Outbox;

/// <summary>
/// Internal intermediate base shared by the two consumer-facing grain roots
/// (<c>EdictCommandHandler&lt;TState&gt;</c> and
/// <c>EdictIdempotencyBase&lt;TPayload&gt;</c>): the single home for the
/// <see cref="IOutboxHost"/> adapter, the <see cref="IRemindable"/> tick,
/// drain-on-activation, the lazy drain-Reminder bookkeeping and the
/// <c>[StorageProvider]</c> binding. Both roots inherit this so the duplicated
/// host plumbing has one home and one persisted-document contract
/// (ADR 0017 clause (b) outer shared root; ADR 0018 unified envelope).
/// <para>
/// The <see cref="OutboxDrainEngine"/> seam itself is untouched — this is the
/// adapter that the engine drives via <see cref="IOutboxHost"/>; the engine
/// stays a plain class, the grain owns the single <c>WriteStateAsync</c>, the
/// stream provider and the Reminder. Under ADR 0022 the dead-letter slice and
/// its operator-recovery surface are gone: a failing entry at <c>MaxAttempts</c>
/// is promoted to a new <see cref="OutboxEffectKind.PublishEvent"/> entry by
/// the engine, so the grain never holds dead letters in its document.
/// </para>
/// </summary>
[StorageProvider(ProviderName = "edict-state")]
public abstract class EdictDurableConsumerBase<TPayload>
    : Grain<GrainEnvelope<TPayload>>, IOutboxHost, IRemindable
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
        get => State.Outbox;
        set => State.Outbox = value;
    }

    IStreamProvider IOutboxHost.StreamProvider => this.GetStreamProvider("edict");

    string IOutboxHost.GrainKey => this.GetPrimaryKey().ToString();

    string IOutboxHost.GrainTypeName => GetType().FullName ?? GetType().Name;

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

    Task IOutboxHost.DispatchEventAsync(EdictEvent evt) => DispatchEventForOutboxAsync(evt);

    /// <summary>
    /// Hook for the InvokeHandler executor's deferred dispatch (ADR 0023): the
    /// engine routes a drained <see cref="OutboxEffectKind.InvokeHandler"/>
    /// entry's <see cref="EdictEvent"/> back into the host grain through this
    /// method. The shared host root has no consumer-visible dispatch surface,
    /// so the default throws — only an <c>EdictEventHandler</c>'s
    /// idempotent-consumer root overrides this to route into its
    /// <c>DispatchAsync</c> (ADR 0023).
    /// </summary>
    protected virtual Task DispatchEventForOutboxAsync(EdictEvent evt) =>
        throw new NotSupportedException(
            $"{GetType().FullName} does not support deferred InvokeHandler dispatch.");
}
