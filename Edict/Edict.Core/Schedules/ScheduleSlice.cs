using System.Collections.Immutable;
using System.ComponentModel;

namespace Edict.Core.Schedules;

/// <summary>
/// The Schedule slice of the grain-state envelope and its pure, total
/// state-machine transitions. Every transition returns a new slice; none mutate
/// in place and none perform I/O — the host composes them and owns the single
/// grain-state write that makes a move atomic. Persisted state, so a frozen
/// string-literal <c>[Alias]</c> survives a class rename; <c>ORLEANS0010</c> is
/// never suppressed.
/// <para>
/// Transitions are keyed by <see cref="ScheduleEntry.ScheduleId"/>. The
/// <see cref="Active"/> list keeps insertion order. Mirrors
/// <c>OutboxSlice</c>: the timing semantics live here as pure functions over a
/// passed-in <c>now</c>, exhaustively unit-testable without a clock.
/// </para>
/// <para>
/// Public because it rides as a slot on <c>GrainEnvelope&lt;TPayload&gt;</c>,
/// whose public slot properties expose it. Hidden from consumer IntelliSense
/// because the consumer never types this — the framework owns every transition.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[GenerateSerializer]
[Alias("ScheduleSlice")]
public sealed record ScheduleSlice
{
    [Id(0)]
    public ImmutableList<ScheduleEntry> Active { get; init; } = ImmutableList<ScheduleEntry>.Empty;

    /// <summary>Adds a schedule to the tail (insertion order).</summary>
    public ScheduleSlice Add(ScheduleEntry entry) =>
        this with { Active = Active.Add(entry) };

    /// <summary>The schedules whose <see cref="ScheduleEntry.DueAt"/> is now-or-past.</summary>
    public IReadOnlyList<ScheduleEntry> DueEntries(DateTimeOffset now) =>
        Active.Where(entry => entry.DueAt <= now).ToArray();

    /// <summary>
    /// The earliest <see cref="ScheduleEntry.DueAt"/> across active schedules, or
    /// <c>null</c> when none are active. The host arms its timer against this; the
    /// Test Framework advances its virtual clock to it.
    /// </summary>
    public DateTimeOffset? SoonestDueAt =>
        Active.Count == 0 ? null : Active.Min(entry => entry.DueAt);

    /// <summary>
    /// Applies a <c>Continue</c> outcome to the schedule with
    /// <paramref name="scheduleId"/>: advances its <see cref="ScheduleEntry.DueAt"/>
    /// to the first cadence-grid instant strictly after <paramref name="now"/>.
    /// Every period that elapsed during a deactivation collapses into this one
    /// forward jump, so a grain that slept through many periods fires once and
    /// resumes on-grid rather than replaying a backlog. Total: a no-op when no
    /// schedule matches.
    /// </summary>
    public ScheduleSlice Continue(Guid scheduleId, DateTimeOffset now)
    {
        var index = IndexOf(scheduleId);
        if (index < 0)
        {
            return this;
        }

        var entry = Active[index];
        return this with { Active = Active.SetItem(index, entry with { DueAt = NextDueAfter(entry, now) }) };
    }

    /// <summary>
    /// Applies a <c>Complete</c> outcome to the schedule with
    /// <paramref name="scheduleId"/>: removes it so it never fires again. Total: a
    /// no-op when no schedule matches.
    /// </summary>
    public ScheduleSlice Complete(Guid scheduleId)
    {
        var index = IndexOf(scheduleId);
        if (index < 0)
        {
            return this;
        }

        return this with { Active = Active.RemoveAt(index) };
    }

    static DateTimeOffset NextDueAfter(ScheduleEntry entry, DateTimeOffset now)
    {
        var period = entry.Period;
        if (period <= TimeSpan.Zero)
        {
            return now;
        }

        var elapsed = now - entry.DueAt;
        if (elapsed < TimeSpan.Zero)
        {
            return entry.DueAt + period;
        }

        var periodsToSkip = (elapsed.Ticks / period.Ticks) + 1;
        return entry.DueAt + new TimeSpan(period.Ticks * periodsToSkip);
    }

    int IndexOf(Guid scheduleId)
    {
        for (var i = 0; i < Active.Count; i++)
        {
            if (Active[i].ScheduleId == scheduleId)
            {
                return i;
            }
        }
        return -1;
    }
}
