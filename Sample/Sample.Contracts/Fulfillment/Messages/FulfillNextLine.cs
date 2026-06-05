using Edict.Contracts.Schedules;

namespace Sample.Contracts.Fulfillment.Messages;

/// <summary>
/// The schedule message the fulfillment aggregate fires on each cadence. Carries
/// no data — the next pending line lives in durable aggregate State, read fresh
/// each fire. Each fire fulfils one line; the schedule completes once every line
/// is fulfilled.
/// </summary>
public sealed partial record FulfillNextLine : EdictScheduleMessage;
