using Edict.Testing;

using Sample.Contracts.Fulfillment.Commands;
using Sample.Contracts.Fulfillment.Events;
using Sample.Domain.Orders.CommandHandlers;

using Xunit;

namespace Sample.Silo.Tests.Fulfillment;

/// <summary>
/// Drives the grain-timer-backed fulfillment handler under a virtual
/// <c>FakeTimeProvider</c>: each <c>AdvanceClock</c> elapses the Orleans grain
/// timer's randomised 2–8s due time, so five clock advances tick all five
/// lines to Fulfilled and the terminal tick raises
/// <c>OrderFullyFulfilledEvent</c> before the timer stops itself.
/// </summary>
public sealed class FulfillmentCommandHandlerTests
{
    [Fact]
    public async Task StartFulfillment_ShouldFulfillAllLinesAcrossFiveTicks_AndRaiseOrderFullyFulfilled()
    {
        var orderId = Guid.Parse("f0000000-0000-0000-0000-000000000001");
        var lineItemIds = Enumerable.Range(0, 5)
            .Select(i => Guid.Parse($"f0000000-0000-0000-0000-00000000010{i}"))
            .ToArray();

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new StartFulfillmentCommand(orderId, lineItemIds));
        await app.Drain();

        // Five advances large enough to cover the max randomised due time (7s);
        // each elapses one grain-timer tick, transitions one Pending line to
        // Fulfilled, then re-arms (or stops on the final tick).
        for (var i = 0; i < 5; i++)
        {
            await app.AdvanceClock(TimeSpan.FromSeconds(10));
        }

        var fulfilledCount = app.Timeline.Entries.Count(e =>
            e.Kind == "Event" && e.Type == nameof(LineItemFulfilledEvent));
        var fullyFulfilledCount = app.Timeline.Entries.Count(e =>
            e.Kind == "Event" && e.Type == nameof(OrderFullyFulfilledEvent));

        Assert.Equal(5, fulfilledCount);
        Assert.Equal(1, fullyFulfilledCount);

        // A sixth advance must not produce any more ticks — the terminal tick
        // disposes the timer so the workflow is terminal.
        await app.AdvanceClock(TimeSpan.FromSeconds(10));
        Assert.Equal(5, app.Timeline.Entries.Count(e =>
            e.Kind == "Event" && e.Type == nameof(LineItemFulfilledEvent)));
    }
}
