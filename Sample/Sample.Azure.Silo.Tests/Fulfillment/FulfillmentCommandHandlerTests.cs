using Edict.Testing;

using Sample.Contracts.Fulfillment.Commands;
using Sample.Contracts.Fulfillment.Events;
using Sample.Domain.Orders.CommandHandlers;

using Xunit;

namespace Sample.Azure.Silo.Tests.Fulfillment;

/// <summary>
/// Drives the EdictSchedule-backed fulfillment handler through the in-memory Test
/// Framework: StartFulfillment schedules a <c>FulfillNextLine</c> from inside
/// HandleAsync, and each interval-agnostic FireDueSchedulesAsync drives one fire
/// that fulfils the next pending line and raises <c>LineItemFulfilledEvent</c>.
/// The terminal fire raises <c>OrderFullyFulfilledEvent</c> and completes the
/// schedule. The test never names the 2s cadence — it just fires until done.
/// </summary>
public sealed class FulfillmentCommandHandlerTests
{
    [Fact]
    public async Task StartFulfillment_FiredToCompletion_FulfillsEveryLineThenRaisesFullyFulfilled_AndThenStops()
    {
        var orderId = Guid.Parse("f0000000-0000-0000-0000-000000000001");
        var lineItemIds = Enumerable.Range(0, 5)
            .Select(i => Guid.Parse($"f0000000-0000-0000-0000-00000000010{i}"))
            .ToArray();

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.SendAsync(new StartFulfillmentCommand(orderId, lineItemIds));
        await app.Drain();

        // One interval-agnostic fire per line walks the workflow to fully
        // fulfilled; the cadence declared at Schedule(...) is never named here.
        for (var line = 0; line < lineItemIds.Length; line++)
        {
            await app.FireDueSchedulesAsync();
        }

        var fulfilledCount = app.Timeline.Entries.Count(entry =>
            entry.Kind == "Event" && entry.Type == nameof(LineItemFulfilledEvent));
        var fullyFulfilledCount = app.Timeline.Entries.Count(entry =>
            entry.Kind == "Event" && entry.Type == nameof(OrderFullyFulfilledEvent));

        Assert.Equal(5, fulfilledCount);
        Assert.Equal(1, fullyFulfilledCount);

        // A sixth fire is a no-op — Complete() stopped the schedule.
        await app.FireDueSchedulesAsync();
        Assert.Equal(5, app.Timeline.Entries.Count(entry =>
            entry.Kind == "Event" && entry.Type == nameof(LineItemFulfilledEvent)));
    }
}
