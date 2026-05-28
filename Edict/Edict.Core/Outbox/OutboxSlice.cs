using System.Collections.Immutable;

using Edict.Contracts.Configuration;

namespace Edict.Core.Outbox;

/// <summary>
/// The Outbox slice of the grain-state envelope and its pure, total
/// state-machine transitions. Every transition returns a new slice; none mutate
/// in place and none perform I/O — the engine composes them and owns the single
/// grain-state write that makes a move atomic. Persisted state, so a frozen
/// string-literal <c>[Alias]</c> survives a class rename;
/// <c>ORLEANS0010</c> is never suppressed.
/// <para>
/// Transitions are keyed by <see cref="OutboxEntry.EntryId"/> rather than head
/// position — no FIFO stop-at-head. The <see cref="Pending"/> list still keeps
/// insertion order — that is the order the drain walks — but no entry is
/// privileged. A failing entry stays in place and is gated by
/// <see cref="OutboxEntry.NextAttemptUtc"/>; the drain continues past it.
/// </para>
/// </summary>
[GenerateSerializer]
[Alias("OutboxSlice")]
public sealed record OutboxSlice
{
    [Id(0)]
    public ImmutableList<OutboxEntry> Pending { get; init; } = ImmutableList<OutboxEntry>.Empty;

    /// <summary>Appends an effect to the tail (insertion order).</summary>
    public OutboxSlice Enqueue(OutboxEntry entry) =>
        this with { Pending = Pending.Add(entry) };

    /// <summary>
    /// Removes the entry with <paramref name="entryId"/> after a successful
    /// drain. Total: a no-op when no entry matches.
    /// </summary>
    public OutboxSlice Ack(Guid entryId)
    {
        var index = IndexOf(entryId);
        if (index < 0)
        {
            return this;
        }

        return this with { Pending = Pending.RemoveAt(index) };
    }

    /// <summary>
    /// Records a failed drain of the entry with <paramref name="entryId"/>:
    /// bumps its <c>AttemptCount</c> and gates the next attempt via
    /// <see cref="OutboxBackoff"/>. The entry stays at the same position in
    /// <see cref="Pending"/> (insertion order is preserved); the drain walks
    /// past it without privilege. Promotion is the caller's
    /// separate decision once attempts are exhausted. Total: a no-op when no
    /// entry matches.
    /// </summary>
    public OutboxSlice FailWithBackoff(Guid entryId, DateTimeOffset now, EdictOptions options)
    {
        var index = IndexOf(entryId);
        if (index < 0)
        {
            return this;
        }

        var failing = Pending[index];
        var attempt = failing.AttemptCount + 1;
        var bumped = failing with
        {
            AttemptCount = attempt,
            NextAttemptUtc = OutboxBackoff.NextAttemptUtc(attempt, now, failing.EntryId, options),
        };

        return this with { Pending = Pending.SetItem(index, bumped) };
    }

    /// <summary>
    /// Removes the entry with <paramref name="entryId"/> and appends
    /// <paramref name="promoted"/> at the tail — the dead-letter promotion
    /// transition the engine commits in one grain-state write. Both
    /// mutations land together so the original effect and the dead-letter
    /// notification cannot disagree. Total: a no-op when no entry matches (the
    /// caller's exhaustion check is the gate).
    /// </summary>
    public OutboxSlice Promote(Guid entryId, OutboxEntry promoted)
    {
        var index = IndexOf(entryId);
        if (index < 0)
        {
            return this;
        }

        return this with { Pending = Pending.RemoveAt(index).Add(promoted) };
    }

    int IndexOf(Guid entryId)
    {
        for (var i = 0; i < Pending.Count; i++)
        {
            if (Pending[i].EntryId == entryId)
            {
                return i;
            }
        }
        return -1;
    }
}
