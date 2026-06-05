using Edict.Contracts.Schedules;
using Edict.Core.Sagas;

using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Orders.Events;
using Sample.Contracts.Reservations.Messages;

namespace Sample.Domain.Reservations.Sagas;

/// <summary>
/// Holds a reservation against a placed order for a fixed window. Placing an order
/// arms the hold; if the order is not confirmed before the window elapses, the
/// hold's timeout cap fires and <see cref="OnScheduleTimeoutAsync"/> dispatches the
/// compensating <see cref="CancelOrderCommand"/>, releasing the hold.
/// <para>
/// The compensation is permissive: the cancel-on-timeout races the payment Saga's
/// confirm, and the order handler's terminal-status guard governs who wins. A hold
/// that fires after the order has already confirmed dispatches a cancel that the
/// order rejects without reopening it.
/// </para>
/// </summary>
public partial class ReservationSaga : EdictSaga<ReservationProgress>
{
    // The hold is a one-shot cap, not a recurring poll: its only meaningful fire is
    // the timeout. There is no per-tick work and no fire handler, so the recurring
    // cadence is parked far past the hold window and no due fire precedes the cap.
    static readonly TimeSpan HoldWindow = TimeSpan.FromMinutes(15);
    static readonly TimeSpan CadenceBeyondTheHold = TimeSpan.FromDays(1);

    Task HandleAsync(OrderPlacedEvent edictEvent)
    {
        Progress.OrderId = edictEvent.OrderId;
        Schedule(new ReservationHoldExpiry(), every: CadenceBeyondTheHold, timeout: HoldWindow);
        return Task.CompletedTask;
    }

    Task OnScheduleTimeoutAsync(ReservationHoldExpiry message)
    {
        Dispatch(new CancelOrderCommand(Progress.OrderId, "reservation_expired"));
        Complete();
        return Task.CompletedTask;
    }
}
