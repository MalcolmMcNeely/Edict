using Edict.Contracts.Schedules;

namespace Sample.Contracts.Watchdog.Messages;

/// <summary>
/// The schedule message a watchdog fires on each poll. Carries no data — what it
/// polls lives in durable aggregate State. The watchdog declares a timeout, so a
/// poll loop that never settles hits its cap and runs
/// <c>OnScheduleTimeoutAsync</c> to escalate.
/// </summary>
public sealed partial record WatchdogPoll : EdictScheduleMessage;
