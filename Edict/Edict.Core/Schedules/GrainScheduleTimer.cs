using Orleans.Runtime;

namespace Edict.Core.Schedules;

// Production IScheduleTimer: a single re-armable Orleans grain timer that fires
// once at the soonest due instant. Sub-minute precision the one-minute Reminder
// floor cannot give; the Reminder remains the durability backstop across
// deactivation. Non-periodic (Period = infinite) — the host re-arms after every
// fire from the recomputed soonest-due, so a coalesced forward jump re-times the
// timer rather than ticking on a fixed grid.
sealed class GrainScheduleTimer(Grain grain) : IScheduleTimer
{
    IGrainTimer? _timer;

    public void Arm(TimeSpan dueTime, Func<CancellationToken, Task> callback)
    {
        _timer?.Dispose();
        _timer = grain.RegisterGrainTimer(callback, new GrainTimerCreationOptions
        {
            DueTime = dueTime < TimeSpan.Zero ? TimeSpan.Zero : dueTime,
            Period = Timeout.InfiniteTimeSpan,
            KeepAlive = true,
        });
    }

    public void Disarm()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
