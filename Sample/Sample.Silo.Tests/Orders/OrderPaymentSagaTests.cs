using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Orders.Projections;
using Sample.Silo.Orders;
using Sample.Silo.Payments;

using Xunit;

namespace Sample.Silo.Tests.Orders;

public sealed class OrderPaymentSagaTests
{
    [Fact]
    public async Task OrderPaymentSaga_ShouldReachConfirmed_WhenAmountIsBelowDeclineThreshold()
    {
        var orderId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId));
        await app.Send(new AddLineItemCommand(orderId, "SKU-1", 1));
        // Below the decline threshold so the saga's happy branch fires.
        await app.Send(new SubmitOrderCommand(orderId, Amount: 100m));
        await app.Drain();

        var progress = await app.GetSagaProgress<OrderPaymentSaga, OrderPaymentProgress>(orderId);
        var row = await app.GetProjectionRow<OrderStatusRow>(
            tableName: "ordersbystatus",
            partitionKey: orderId.ToString(),
            rowKey: "status");

        await Verify(new { progress, row });
    }

    [Fact]
    public async Task OrderPaymentSaga_ShouldReachCompensated_WhenAmountIsAboveDeclineThreshold()
    {
        var orderId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId));
        await app.Send(new AddLineItemCommand(orderId, "SKU-1", 1));
        // Above the PaymentCommandHandler's DeclineThreshold so the saga's
        // compensation branch (PaymentDeclined → CancelOrder) fires.
        await app.Send(new SubmitOrderCommand(orderId, Amount: 5_000m));
        await app.Drain();

        var progress = await app.GetSagaProgress<OrderPaymentSaga, OrderPaymentProgress>(orderId);
        var row = await app.GetProjectionRow<OrderStatusRow>(
            tableName: "ordersbystatus",
            partitionKey: orderId.ToString(),
            rowKey: "status");

        await Verify(new { progress, row });
    }
}
