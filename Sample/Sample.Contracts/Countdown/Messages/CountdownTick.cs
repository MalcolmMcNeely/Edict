using Edict.Contracts.Schedules;

namespace Sample.Contracts.Countdown.Messages;

/// <summary>
/// The schedule message a countdown fires on each cadence. Carries no data — the
/// remaining count lives in durable aggregate State, read fresh each fire.
/// </summary>
public sealed partial record CountdownTick : EdictScheduleMessage;
