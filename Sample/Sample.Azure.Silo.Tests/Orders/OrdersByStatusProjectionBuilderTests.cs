using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Orders.Projections;
using Sample.Domain.Orders.CommandHandlers;

using Xunit;

namespace Sample.Azure.Silo.Tests.Orders;

/// <summary>
/// Per-state-transition tests for the five timestamp columns added to
/// <see cref="OrderStatusRow"/>. Each test drives the lifecycle up to its
/// target transition and asserts the matching column is stamped — columns
/// for transitions that have not yet fired remain null. Status itself is
/// chaos-reorder variant (the last status-mutating event to land at the
/// projection wins), so only the timestamp columns are asserted.
/// </summary>
public sealed class OrdersByStatusProjectionBuilderTests
{
    [Fact]
    public async Task Handle_OrderSubmittedEvent_StampsSubmittedAt_AndLaterColumnsStayNull()
    {
        var orderId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Send(new AddLineItemCommand(orderId, Guid.NewGuid(), "SKU-1", 1));
        // Above PaymentCommandHandler.DeclineThreshold so the OrderPayment saga's
        // compensation branch (PaymentDeclined → CancelOrder) fires — neither
        // PaymentAuthorized nor OrderFullyFulfilled/OrderShipped ever land.
        await app.Send(new SubmitOrderCommand(orderId, Amount: 5_000m));
        await app.Drain();

        var row = await GetRow(app, orderId);
        Assert.NotNull(row);
        Assert.Equal(1, row.ItemCount);
        Assert.NotNull(row.PlacedAt);
        Assert.NotNull(row.SubmittedAt);
        Assert.Null(row.AuthorizedAt);
        Assert.Null(row.FulfilledAt);
        Assert.Null(row.ShippedAt);
    }

    [Fact]
    public async Task Handle_PaymentAuthorizedEvent_StampsAuthorizedAt_AndLaterColumnsStayNull()
    {
        var orderId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Send(new AddLineItemCommand(orderId, Guid.NewGuid(), "SKU-1", 1));
        // Below decline threshold — the saga authorises payment. No AdvanceClock
        // so the fulfillment grain timer never ticks; OrderFullyFulfilled and
        // OrderShipped do not fire.
        await app.Send(new SubmitOrderCommand(orderId, Amount: 100m));
        await app.Drain();

        var row = await GetRow(app, orderId);
        Assert.NotNull(row);
        Assert.Equal(1, row.ItemCount);
        Assert.NotNull(row.PlacedAt);
        Assert.NotNull(row.SubmittedAt);
        Assert.NotNull(row.AuthorizedAt);
        Assert.Null(row.FulfilledAt);
        Assert.Null(row.ShippedAt);
    }

    [Fact]
    public async Task FullLifecycle_PopulatesAllFiveTimestampsInOrder_WithShippedStatus()
    {
        var orderId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Send(new AddLineItemCommand(orderId, Guid.NewGuid(), "SKU-1", 1));
        await app.Send(new AddLineItemCommand(orderId, Guid.NewGuid(), "SKU-2", 1));
        await app.Send(new AddLineItemCommand(orderId, Guid.NewGuid(), "SKU-3", 1));
        await app.Send(new SubmitOrderCommand(orderId, Amount: 100m));
        await app.Drain();
        for (var i = 0; i < 3; i++)
        {
            await app.AdvanceClock(TimeSpan.FromSeconds(10));
        }

        var row = await GetRow(app, orderId);
        Assert.NotNull(row);
        Assert.Equal("Shipped", row.Status);
        Assert.NotNull(row.PlacedAt);
        Assert.NotNull(row.SubmittedAt);
        Assert.NotNull(row.AuthorizedAt);
        Assert.NotNull(row.FulfilledAt);
        Assert.NotNull(row.ShippedAt);
        Assert.True(row.PlacedAt <= row.SubmittedAt);
        Assert.True(row.SubmittedAt <= row.AuthorizedAt);
        Assert.True(row.AuthorizedAt <= row.FulfilledAt);
        Assert.True(row.FulfilledAt <= row.ShippedAt);
    }

    static Task<OrderStatusRow?> GetRow(EdictTestApp app, Guid orderId) =>
        app.GetProjectionRow<OrderStatusRow>(
            tableName: "ordersbystatus",
            partitionKey: orderId.ToString(),
            rowKey: "status");
}
