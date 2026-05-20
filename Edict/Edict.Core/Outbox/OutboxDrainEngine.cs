using Edict.Contracts.Configuration;
using Edict.Core.DeadLetter;

namespace Edict.Core.Outbox;

/// <summary>
/// The single Outbox engine (ADR 0018 / 0022). Composes the pure
/// <see cref="OutboxSlice"/> transitions, owns the drain loop, and decides the
/// lazy Reminder lifecycle. Drain is <b>FIFO, stop-at-head</b> (per-aggregate
/// causal order) and <b>awaited inline immediately after the commit</b>, so the
/// happy-path span tree is identical to the pre-Outbox code. A post-commit
/// effect failure bumps backoff, stops at the head, and leaves the Reminder as
/// the durable retry — it never rolls back nor surfaces to the caller.
/// At <c>MaxAttempts</c> the engine promotes the failing head via
/// <see cref="IDeadLetterPromoter"/>: the failing entry is removed and a new
/// <see cref="OutboxEffectKind.PublishEvent"/> entry carrying an
/// <c>EdictDeadLetterRaised</c> notification is appended at the FIFO tail, in
/// the same one grain-state write — there is no in-grain dead-letter slice.
/// Grain-coupled work goes through the <see cref="IOutboxHost"/> seam, so the
/// engine itself is a plain class. Bare-named — no consumer types it.
/// </summary>
sealed class OutboxDrainEngine
{
    readonly IReadOnlyDictionary<OutboxEffectKind, IOutboxEffectExecutor> _executors;
    readonly TimeProvider _timeProvider;
    readonly EdictOutboxOptions _options;
    readonly IDeadLetterPromoter _promoter;

    public OutboxDrainEngine(
        IEnumerable<IOutboxEffectExecutor> executors,
        TimeProvider timeProvider,
        EdictOutboxOptions options,
        IDeadLetterPromoter promoter)
    {
        _executors = executors.ToDictionary(static e => e.Kind);
        _timeProvider = timeProvider;
        _options = options;
        _promoter = promoter;
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
                host.Outbox = host.Outbox.FailHeadWithBackoff(now, _options);

                if (host.Outbox.Pending[0].AttemptCount >= _options.MaxAttempts)
                {
                    var promoted = _promoter.Promote(
                        host.Outbox.Pending[0], exception, host.GrainKey, host.GrainTypeName, now);
                    host.Outbox = host.Outbox.PromoteHead(promoted);
                    await host.CommitAsync();
                    continue;
                }

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
