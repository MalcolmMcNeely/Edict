using Edict.Contracts.Configuration;

namespace Edict.Core.Outbox;

/// <summary>
/// The Outbox slice of the grain-state envelope and its pure, total
/// state-machine transitions (ADR 0018 / 0022). Every transition returns a new
/// slice; none mutate in place and none perform I/O — the engine composes them
/// and owns the single grain-state write that makes a move atomic. Persisted
/// state, so a frozen string-literal <c>[Alias]</c> survives a class rename
/// (ADR 0017); <c>ORLEANS0010</c> is never suppressed. The pre-ADR-0022
/// in-grain <c>DeadLetter</c> slot is removed; dead-lettering is now a
/// promotion of the failing head into a new <c>PublishEvent</c> entry
/// appended at the FIFO tail via <see cref="PromoteHead"/>.
/// </summary>
[GenerateSerializer]
[Alias("OutboxSlice")]
public sealed record OutboxSlice
{
    [Id(0)]
    public List<OutboxEntry> Pending { get; init; } = [];

    /// <summary>Appends an effect to the FIFO tail.</summary>
    public OutboxSlice Enqueue(OutboxEntry entry) =>
        this with { Pending = [.. Pending, entry] };

    /// <summary>
    /// Removes the FIFO head after a successful drain. Total: a no-op when
    /// nothing is pending.
    /// </summary>
    public OutboxSlice AckHead() =>
        Pending.Count == 0 ? this : this with { Pending = [.. Pending.Skip(1)] };

    /// <summary>
    /// Records a failed drain of the head: bumps its <c>AttemptCount</c> and
    /// gates the next attempt via <see cref="OutboxBackoff"/>. The entry stays
    /// at the FIFO head (stop-at-head ordering); promotion is the caller's
    /// separate decision once attempts are exhausted. Total: a no-op when
    /// nothing is pending.
    /// </summary>
    public OutboxSlice FailHeadWithBackoff(DateTimeOffset now, EdictOutboxOptions options)
    {
        if (Pending.Count == 0)
        {
            return this;
        }

        var head = Pending[0];
        var attempt = head.AttemptCount + 1;
        var bumped = head with
        {
            AttemptCount = attempt,
            NextAttemptUtc = OutboxBackoff.NextAttemptUtc(attempt, now, head.EntryId, options),
        };

        return this with { Pending = [bumped, .. Pending.Skip(1)] };
    }

    /// <summary>
    /// Removes the FIFO head and appends <paramref name="promoted"/> at the
    /// tail — the dead-letter promotion transition the engine commits in one
    /// grain-state write (ADR 0022). Both mutations land together so the
    /// original effect and the dead-letter notification cannot disagree.
    /// Total: a no-op when nothing is pending (the caller's exhaustion check
    /// is the gate).
    /// </summary>
    public OutboxSlice PromoteHead(OutboxEntry promoted)
    {
        if (Pending.Count == 0)
        {
            return this;
        }

        return this with { Pending = [.. Pending.Skip(1), promoted] };
    }
}
