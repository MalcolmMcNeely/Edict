using Edict.Contracts.Schedules;

namespace Sample.Contracts.Reservations.Messages;

/// <summary>
/// The schedule message a reservation hold fires when its cap elapses. Carries no
/// data: the held order's id lives in durable saga Progress, read fresh on the
/// fire. The hold is a one-shot, so only its timeout cap is meaningful; there is
/// no per-tick work and no fire handler.
/// </summary>
public sealed partial record ReservationHoldExpiry : EdictScheduleMessage;
