using Edict.Contracts.Schedules;

namespace Sample.Contracts.Delivery.Messages;

/// <summary>
/// The schedule message a delivery fires on each cadence. Carries no data — the
/// remaining ETA lives in durable aggregate State, read fresh each fire.
/// </summary>
public sealed partial record DeliveryEtaTick : EdictScheduleMessage;
