using Edict.Testing;

using Sample.Contracts.Delivery.Commands;
using Sample.Contracts.Delivery.Events;
using Sample.Domain.Delivery.CommandHandlers;

using Xunit;

namespace Sample.Azure.Silo.Tests.Delivery;

/// <summary>
/// Drives the EdictSchedule surface end-to-end through the in-memory Test
/// Framework: shipping arms a recurring tick from inside HandleAsync, and each
/// interval-agnostic FireDueSchedulesAsync drives one fire that routes back to
/// HandleAsync(DeliveryEtaTick), ticking the ETA down a day and raising its
/// tracking event onto the Timeline. Continue re-arms; on arrival Complete stops.
/// </summary>
public sealed class DeliveryTrackingTests
{
    [Fact]
    public async Task ShippedOrderTicksTheDeliveryEtaOnEachRecurringFire()
    {
        var orderId = Guid.Parse("d0000000-0000-0000-0000-000000000001");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(DeliveryTrackerCommandHandler).Assembly));

        await app.SendAsync(new StartDeliveryTrackingCommand(orderId, EtaDays: 2));
        await app.Drain();

        // Two fires walk the ETA to arrival, each interval-agnostic.
        await app.FireDueSchedulesAsync();
        await app.FireDueSchedulesAsync();

        var ticked = app.Timeline.Entries.Count(entry =>
            entry.Kind == "Event" && entry.Type == nameof(DeliveryEtaTickedEvent));

        Assert.Equal(2, ticked);
    }

    [Fact]
    public async Task DeliveredOrderCompletesAndAFurtherFireIsANoOp()
    {
        var orderId = Guid.Parse("d0000000-0000-0000-0000-000000000002");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(DeliveryTrackerCommandHandler).Assembly));

        await app.SendAsync(new StartDeliveryTrackingCommand(orderId, EtaDays: 2));
        await app.Drain();

        // Two fires reach the door: the second raises DeliveredEvent and completes.
        await app.FireDueSchedulesAsync();
        await app.FireDueSchedulesAsync();

        var delivered = app.Timeline.Entries.Count(entry =>
            entry.Kind == "Event" && entry.Type == nameof(DeliveredEvent));

        Assert.Equal(1, delivered);

        // A third fire is a no-op — Complete() stopped the schedule.
        await app.FireDueSchedulesAsync();
        Assert.Equal(2, app.Timeline.Entries.Count(entry =>
            entry.Kind == "Event" && entry.Type == nameof(DeliveryEtaTickedEvent)));
    }
}
