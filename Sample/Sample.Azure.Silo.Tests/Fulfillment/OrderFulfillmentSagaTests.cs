using Edict.Testing;

using Sample.Contracts.Fulfillment.Commands;
using Sample.Contracts.Orders.Commands;
using Sample.Domain.Fulfillment.Sagas;
using Sample.Domain.Orders.CommandHandlers;

using Xunit;

namespace Sample.Azure.Silo.Tests.Fulfillment;

/// <summary>
/// The OrderFulfillment saga is the workflow-handoff Saga shape (complementing
/// the compensation shape of <c>OrderPaymentSaga</c>): exactly one command per
/// event, no fan-out, just a thin bridge between two aggregates. Two tests, one
/// per transition.
/// </summary>
public sealed class OrderFulfillmentSagaTests
{
    [Fact]
    public async Task OrderConfirmed_ShouldDispatchStartFulfillmentCommand()
    {
        var orderId = Guid.Parse("c0000000-0000-0000-0000-000000000001");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        for (var i = 0; i < 5; i++)
        {
            await app.Send(new AddLineItemCommand(orderId, Guid.NewGuid(), $"SKU-{i}", 1));
        }
        // Below the decline threshold so the OrderPayment saga authorises and
        // the order confirms; the new OrderFulfillment saga then bridges
        // OrderConfirmed → StartFulfillment.
        await app.Send(new SubmitOrderCommand(orderId, Amount: 100m));
        await app.Drain();

        var startCount = app.Timeline.Entries.Count(e =>
            e.Kind == "Command" && e.Type == nameof(StartFulfillmentCommand));
        Assert.Equal(1, startCount);
    }

    [Fact]
    public async Task OrderFullyFulfilled_ShouldDispatchMarkOrderShippedCommand()
    {
        var orderId = Guid.Parse("c0000000-0000-0000-0000-000000000002");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        for (var i = 0; i < 5; i++)
        {
            await app.Send(new AddLineItemCommand(orderId, Guid.NewGuid(), $"SKU-{i}", 1));
        }
        await app.Send(new SubmitOrderCommand(orderId, Amount: 100m));
        await app.Drain();

        // Five timer ticks transition all lines to Fulfilled; the terminal
        // tick raises OrderFullyFulfilledEvent which the saga bridges into
        // MarkOrderShippedCommand.
        for (var i = 0; i < 5; i++)
        {
            await app.AdvanceClock(TimeSpan.FromSeconds(10));
        }

        var markShippedCount = app.Timeline.Entries.Count(e =>
            e.Kind == "Command" && e.Type == nameof(MarkOrderShippedCommand));
        Assert.Equal(1, markShippedCount);

        var progress = await app.GetSagaProgress<OrderFulfillmentSaga, OrderFulfillmentProgress>(orderId);
        Assert.Equal(OrderFulfillmentStage.Shipped, progress.Stage);
    }
}
