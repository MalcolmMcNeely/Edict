using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Orders.Projections;
using Sample.Silo.Orders.CommandHandlers;

using Xunit;

namespace Sample.Silo.Tests.Orders;

/// <summary>
/// Per-state-transition tests for the five timestamp columns added to
/// <see cref="OrderStatusRow"/>. Each test drives the lifecycle up to its
/// target transition and asserts the matching column is stamped with the
/// FakeTimeProvider's value at the moment the event was raised — columns for
/// transitions that have not yet fired remain null. ADR 0026 guarantees that
/// <c>OccurredAt</c> is stamped at <c>Raise()</c> time via the harness's
/// <c>FakeTimeProvider</c>, so the entire same-drain cascade lands at one
/// known fake instant.
/// </summary>
public sealed class OrdersByStatusProjectionBuilderTests
{
    // The harness's FakeTimeProvider starts at this instant
    // (see EdictTestApp.StartAsync).
    static readonly DateTimeOffset HarnessStart =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_OrderPlacedEvent_StampsOnlyPlacedAt()
    {
        var orderId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Drain();

        var row = await GetRow(app, orderId);
        Assert.NotNull(row);
        Assert.Equal(HarnessStart, row.PlacedAt);
        Assert.Null(row.SubmittedAt);
        Assert.Null(row.AuthorizedAt);
        Assert.Null(row.FulfilledAt);
        Assert.Null(row.ShippedAt);
    }

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
        Assert.Equal(HarnessStart, row.SubmittedAt);
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
        Assert.Equal(HarnessStart, row.AuthorizedAt);
        Assert.Null(row.FulfilledAt);
        Assert.Null(row.ShippedAt);
    }

    [Fact]
    public async Task Handle_OrderFullyFulfilledEvent_StampsFulfilledAt()
    {
        var orderId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Send(new AddLineItemCommand(orderId, Guid.NewGuid(), "SKU-1", 1));
        await app.Send(new AddLineItemCommand(orderId, Guid.NewGuid(), "SKU-2", 1));
        await app.Send(new AddLineItemCommand(orderId, Guid.NewGuid(), "SKU-3", 1));
        await app.Send(new SubmitOrderCommand(orderId, Amount: 100m));
        await app.Drain();
        // One advance per line-item tick. The terminal tick fires
        // OrderFullyFulfilled, which the saga immediately bridges into
        // MarkOrderShipped → OrderShipped (same drain, same fake instant).
        for (var i = 0; i < 3; i++)
        {
            await app.AdvanceClock(TimeSpan.FromSeconds(10));
        }

        var row = await GetRow(app, orderId);
        Assert.NotNull(row);
        Assert.NotNull(row.FulfilledAt);
        Assert.True(row.FulfilledAt > HarnessStart,
            $"FulfilledAt {row.FulfilledAt} should be after harness start {HarnessStart}.");
    }

    [Fact]
    public async Task Handle_OrderShippedEvent_StampsShippedAt()
    {
        var orderId = Guid.Parse("55555555-5555-5555-5555-555555555555");

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
        Assert.NotNull(row.ShippedAt);
        Assert.True(row.ShippedAt > HarnessStart,
            $"ShippedAt {row.ShippedAt} should be after harness start {HarnessStart}.");
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
