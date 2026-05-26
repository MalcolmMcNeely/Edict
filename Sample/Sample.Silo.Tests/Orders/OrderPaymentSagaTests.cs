using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Silo.Orders.CommandHandlers;
using Sample.Silo.Payments.Sagas;

using Xunit;

namespace Sample.Silo.Tests.Orders;

// Saga tests assert saga progression only. The OrdersByStatus projection row
// is sensitive to chaos reordering (different runs land events in different
// orders, perturbing transient column visibility) and is independently pinned
// by OrdersByStatusProjectionBuilderTests — capturing it here just imports the
// chaos noise into the saga snapshot.
public sealed class OrderPaymentSagaTests
{
    [Fact]
    public async Task OrderPaymentSaga_ShouldReachConfirmed_WhenAmountIsBelowDeclineThreshold()
    {
        var orderId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Send(new AddLineItemCommand(orderId, Guid.NewGuid(), "SKU-1", 1));
        // Below the decline threshold so the saga's happy branch fires.
        await app.Send(new SubmitOrderCommand(orderId, Amount: 100m));
        await app.Drain();

        var progress = await app.GetSagaProgress<OrderPaymentSaga, OrderPaymentProgress>(orderId);

        await Verify(progress);
    }

    [Fact]
    public async Task OrderPaymentSaga_ShouldReachCompensated_WhenAmountIsAboveDeclineThreshold()
    {
        var orderId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Send(new AddLineItemCommand(orderId, Guid.NewGuid(), "SKU-1", 1));
        // Above the PaymentCommandHandler's DeclineThreshold so the saga's
        // compensation branch (PaymentDeclined → CancelOrder) fires.
        await app.Send(new SubmitOrderCommand(orderId, Amount: 5_000m));
        await app.Drain();

        var progress = await app.GetSagaProgress<OrderPaymentSaga, OrderPaymentProgress>(orderId);

        await Verify(progress);
    }
}
