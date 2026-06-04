namespace Edict.Core.Schedules;

// The grain-timer seam ScheduleHost arms for sub-minute fire precision while a
// schedule is active. Abstracted so the host's arm/disarm reconciliation is
// unit-testable without an Orleans grain; the production implementation wraps
// the grain's RegisterGrainTimer.
interface IScheduleTimer
{
    void Arm(TimeSpan dueTime, Func<CancellationToken, Task> callback);

    void Disarm();
}
