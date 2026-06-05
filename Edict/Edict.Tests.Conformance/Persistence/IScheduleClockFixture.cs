namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// A persistence-axis fixture whose silo runs on a virtual clock, so a schedule
/// scenario can push a schedule's due and deadline instants past-due without a
/// wall-clock wait. The Orleans grain timer still fires on wall-clock time, so a
/// timer armed at <c>dueTime=0</c> on reactivation catches up immediately — which
/// is exactly the on-activation seam the schedule-survival scenario exercises,
/// never the one-minute Reminder.
/// </summary>
public interface IScheduleClockFixture
{
    void AdvanceClock(TimeSpan by);
}
