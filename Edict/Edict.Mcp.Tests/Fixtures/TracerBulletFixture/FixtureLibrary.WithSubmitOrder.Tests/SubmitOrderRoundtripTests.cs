using Edict.Testing;

using FixtureLibrary.WithSubmitOrder.Orders;

using Xunit;

namespace FixtureLibrary.WithSubmitOrder.Tests;

public sealed class SubmitOrderRoundtripTests
{
    [Fact]
    public async Task PlaceThenSubmit_ClosesTheOrder_AndRaisesOrderPlacedAndOrderSubmitted()
    {
        var orderId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await using var app = await EdictTestApp.StartAsync(builder => builder
            .WithConsumer(typeof(SubmitOrderCommand).Assembly));

        await app.Send(new PlaceOrderCommand { OrderId = orderId });
        await app.Send(new SubmitOrderCommand { OrderId = orderId });
        await app.Drain();

        var raisedEvents = app.Timeline.Entries
            .Where(entry => entry.Kind == "Event")
            .Select(entry => entry.Type)
            .ToArray();

        Assert.Contains(nameof(OrderPlaced), raisedEvents);
        Assert.Contains(nameof(OrderSubmittedEvent), raisedEvents);
    }
}
