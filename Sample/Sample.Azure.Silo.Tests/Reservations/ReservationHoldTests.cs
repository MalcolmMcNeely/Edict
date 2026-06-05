using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Orders.Events;
using Sample.Domain.Reservations.Sagas;

using Xunit;

namespace Sample.Azure.Silo.Tests.Reservations;

/// <summary>
/// Drives the reservation hold end-to-end through the in-memory Test Framework: a
/// placed order arms a hold (an EdictSchedule with a timeout) on the
/// ReservationSaga, and the interval-agnostic FireScheduleTimeoutsAsync advances
/// the virtual clock to the cap and fires it, so OnScheduleTimeoutAsync dispatches
/// the compensating CancelOrderCommand without waiting on wall-clock time. The
/// compensation is permissive: it races the payment Saga's confirm, and the order
/// handler's terminal-status guard governs who wins.
/// </summary>
public sealed class ReservationHoldTests
{
    [Fact]
    public async Task ReservationExpiresAndAutoCancels()
    {
        var orderId = Guid.Parse("c0000000-0000-0000-0000-000000000001");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(ReservationSaga).Assembly));

        // Placing the order arms the hold; it is never confirmed.
        await app.SendAsync(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Drain();

        // Interval-agnostic: advance to the hold cap and fire its timeout.
        await app.FireScheduleTimeoutsAsync();

        var cancelled = app.Timeline.Entries.Count(entry =>
            entry.Kind == "Event" && entry.Type == nameof(OrderCancelledEvent));

        Assert.Equal(1, cancelled);

        // The cap is terminal — a second timeout fire cancels nothing more.
        await app.FireScheduleTimeoutsAsync();
        Assert.Equal(1, app.Timeline.Entries.Count(entry =>
            entry.Kind == "Event" && entry.Type == nameof(OrderCancelledEvent)));
    }

    [Fact]
    public async Task ExpiredReservationIsAPermissiveNoOpAgainstAConfirmedOrder()
    {
        var orderId = Guid.Parse("c0000000-0000-0000-0000-000000000002");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(ReservationSaga).Assembly));

        // Placing arms the hold; submitting below the decline threshold drives the
        // payment Saga to confirm the order before the hold ever fires.
        await app.SendAsync(new PlaceOrderCommand(orderId, "REF-001"));
        await app.SendAsync(new AddLineItemCommand(orderId, Guid.NewGuid(), "SKU-1", 1));
        await app.SendAsync(new SubmitOrderCommand(orderId, Amount: 100m));
        await app.Drain();

        // The hold cap fires against a now-confirmed order: the compensation runs
        // (one CancelOrderCommand dispatched) but the order's terminal-status guard
        // rejects it — no OrderCancelledEvent, no throw, no reopen.
        await app.FireScheduleTimeoutsAsync();

        var cancelCommands = app.Timeline.Entries.Count(entry =>
            entry.Kind == "Command" && entry.Type == nameof(CancelOrderCommand));
        var cancelledEvents = app.Timeline.Entries.Count(entry =>
            entry.Kind == "Event" && entry.Type == nameof(OrderCancelledEvent));

        Assert.Equal(1, cancelCommands);
        Assert.Equal(0, cancelledEvents);
    }
}
