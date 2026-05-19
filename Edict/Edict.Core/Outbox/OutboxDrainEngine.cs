namespace Edict.Core.Outbox;

/// <summary>
/// The single Outbox engine (ADR 0018). Composes the pure
/// <see cref="OutboxSlice"/> transitions, owns the drain loop, and decides the
/// lazy Reminder lifecycle. Drain is <b>FIFO, stop-at-head</b> (per-aggregate
/// causal order) and <b>awaited inline immediately after the commit</b>, so the
/// happy-path span tree is identical to the pre-Outbox code. A post-commit
/// effect failure bumps backoff, stops at the head, and leaves the Reminder as
/// the durable retry — it never rolls back nor surfaces to the caller.
/// Grain-coupled work goes through the <see cref="IOutboxHost"/> seam, so the
/// engine itself is a plain class. Bare-named — no consumer types it.
/// </summary>
sealed class OutboxDrainEngine
{
    static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(2);

    readonly IReadOnlyDictionary<OutboxEffectKind, IOutboxEffectExecutor> _executors;
    readonly TimeProvider _timeProvider;

    public OutboxDrainEngine(IEnumerable<IOutboxEffectExecutor> executors, TimeProvider timeProvider)
    {
        _executors = executors.ToDictionary(static e => e.Kind);
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Stages the raised effects onto the Outbox, commits <c>{ State, Outbox }</c>
    /// in one write, then awaits the inline drain. The commit is the durability
    /// point — <c>Send()</c> returns <c>Accepted</c> once it (and the awaited
    /// drain) completes.
    /// </summary>
    public async Task EnqueueAndDrainAsync(IOutboxHost host, IReadOnlyList<OutboxEntry> entries)
    {
        foreach (var entry in entries)
        {
            host.Outbox = host.Outbox.Enqueue(entry);
        }

        await host.CommitAsync();
        await DrainAsync(host);
    }

    /// <summary>
    /// Drains pending effects FIFO, stopping at the head on the first failure
    /// or backoff gate. Reconciles the lazy Reminder: unregistered when the
    /// Outbox fully drains, registered while anything remains.
    /// </summary>
    public async Task DrainAsync(IOutboxHost host)
    {
        while (host.Outbox.Pending.Count > 0)
        {
            var head = host.Outbox.Pending[0];
            var now = _timeProvider.GetUtcNow();

            if (head.NextAttemptUtc > now)
            {
                break; // backoff-gated; stop-at-head
            }

            try
            {
                await _executors[head.Kind].ExecuteAsync(head, host.StreamProvider);
            }
            catch
            {
                // Post-commit failure: do not roll back, do not surface. Bump
                // backoff, stop at the head (causal order), let the Reminder retry.
                host.Outbox = host.Outbox.FailHeadWithBackoff(now, BaseBackoff);
                await host.CommitAsync();
                break;
            }

            host.Outbox = host.Outbox.AckHead();
            await host.CommitAsync();
        }

        if (host.Outbox.Pending.Count == 0)
        {
            await host.UnregisterDrainReminderAsync();
        }
        else
        {
            await host.RegisterDrainReminderAsync();
        }
    }
}
