using Edict.Contracts.Configuration;
using Edict.Core.DeadLetter;

namespace Edict.Core.Outbox;

/// <summary>
/// The <c>{ Pending, DeadLetter }</c> slice of the grain-state envelope and its
/// pure, total state-machine transitions (ADR 0018 / 0019). Every transition
/// returns a new slice; none mutate in place and none perform I/O — the engine
/// (a later slice) composes them and owns the single grain-state write that
/// makes the move atomic. Persisted state, so a frozen string-literal
/// <c>[Alias]</c> (ADR 0017); <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("OutboxSlice")]
public sealed record OutboxSlice
{
    [Id(0)]
    public List<OutboxEntry> Pending { get; init; } = [];

    [Id(1)]
    public List<DeadLetterEntry> DeadLetter { get; init; } = [];

    /// <summary>
    /// True once the <see cref="DeadLetter"/> slice has reached the configured
    /// cap (ADR 0019). The grain blocks intake while this holds — commands
    /// surface an infrastructure fault and redelivered events are not acked —
    /// so nothing is ever silently dropped until an operator redrives. Pure,
    /// total: a plain count comparison, no I/O.
    /// </summary>
    public bool IsIntakeBlocked(int deadLetterCap) => DeadLetter.Count >= deadLetterCap;

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
    /// at the FIFO head (stop-at-head ordering); dead-lettering is the caller's
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
    /// Moves the FIFO head out of <see cref="Pending"/> into
    /// <see cref="DeadLetter"/> — the same atomic transition the engine commits
    /// in one grain-state write (ADR 0019). Frees the tail so a poison head
    /// never blocks permanently. Total: a no-op when nothing is pending.
    /// </summary>
    public OutboxSlice DeadLetterHead(DateTimeOffset now, string? reason)
    {
        if (Pending.Count == 0)
        {
            return this;
        }

        var dead = new DeadLetterEntry
        {
            Entry = Pending[0],
            DeadLetteredAt = now,
            Reason = reason,
        };

        return this with
        {
            Pending = [.. Pending.Skip(1)],
            DeadLetter = [.. DeadLetter, dead],
        };
    }

    /// <summary>
    /// Operator recovery: moves a dead-lettered entry back to the FIFO tail
    /// with its attempt counter reset (ADR 0019). Total: a no-op when no
    /// dead-letter entry has the given id.
    /// </summary>
    public OutboxSlice Redrive(Guid entryId, DateTimeOffset now)
    {
        var index = DeadLetter.FindIndex(dead => dead.Entry.EntryId == entryId);
        if (index < 0)
        {
            return this;
        }

        var revived = DeadLetter[index].Entry with { AttemptCount = 0, NextAttemptUtc = now };

        return this with
        {
            DeadLetter = [.. DeadLetter.Where((_, i) => i != index)],
            Pending = [.. Pending, revived],
        };
    }
}
