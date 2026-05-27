using Edict.Contracts.Commands;
using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Orders.Events;
using Sample.Domain.Orders.CommandHandlers;

using Xunit;

namespace Sample.Silo.Tests.Orders;

public sealed class MarkOrderShippedTests
{
    [Fact]
    public async Task MarkOrderShipped_ShouldRaiseOrderShippedEvent_WhenOrderIsConfirmed()
    {
        var orderId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var lineItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Send(new AddLineItemCommand(orderId, lineItemId, "SKU-1", 1));
        await app.Send(new SubmitOrderCommand(orderId, Amount: 100m));
        await app.Drain();

        var result = await app.Send(new MarkOrderShippedCommand(orderId));
        await app.Drain();

        Assert.IsType<EdictCommandResult.Accepted>(result);
        Assert.Contains(
            app.Timeline.Entries,
            e => e.Kind == "Event" && e.Type == nameof(OrderShippedEvent));
    }

    [Fact]
    public async Task MarkOrderShipped_ShouldReject_WhenOrderIsNotConfirmed()
    {
        var orderId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Drain();

        var result = await app.Send(new MarkOrderShippedCommand(orderId));

        var rejected = Assert.IsType<EdictCommandResult.Rejected>(result);
        var reason = Assert.Single(rejected.Reasons);
        Assert.Equal("order_not_confirmed", reason.Code);
    }
}
