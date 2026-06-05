using Edict.Testing;

using Sample.Contracts.Fulfillment.Commands;
using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Orders.Events;
using Sample.Contracts.Orders.Projections;
using Sample.Contracts.Payments.Commands;
using Sample.Domain.Orders.CommandHandlers;
using Sample.Domain.Orders.Sagas;

using Xunit;

namespace Sample.Azure.Silo.Tests.Orders;

/// <summary>
/// The OrderClosure saga is the cross-aggregate barrier shape: it accumulates one
/// bool per join arm and dispatches nothing until <em>both</em> payment
/// authorization and full fulfillment have landed, in either arrival order, then
/// dispatches a single <see cref="CloseOrderCommand"/> and completes. Behavior is
/// observed through consumer-facing surfaces only: the dispatched Command on the
/// timeline, the projection the close drove, and the saga's durable Progress.
/// <para>
/// The projected <c>Status</c> string is chaos-reorder variant (the last
/// status-mutating event to land at the projection wins, and the close races
/// MarkOrderShipped on the same trigger Event), so the deterministic close signal
/// is the stamped <see cref="OrderStatusRow.ClosedAt"/> column plus the
/// <see cref="OrderClosedEvent"/> on the timeline, mirroring
/// <c>OrdersByStatusProjectionBuilderTests</c>.
/// </para>
/// </summary>
public sealed class OrderClosureSagaTests
{
    [Fact]
    public async Task PaymentAuthorizedAlone_RecordsArm_AndDispatchesNothing()
    {
        var orderId = Guid.Parse("d0000000-0000-0000-0000-000000000001");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        // Authorize payment in isolation (below the decline threshold) so only the
        // payment arm of the barrier lands; no schedule is fired, so fulfillment
        // never completes.
        await app.SendAsync(new AuthorizePaymentCommand(orderId, Amount: 100m));
        await app.Drain();

        Assert.Equal(0, CloseOrderCommandCount(app));
        Assert.Null((await GetRow(app, orderId))?.ClosedAt);

        var progress = await app.GetSagaProgress<OrderClosureSaga, OrderClosureProgress>(orderId);
        await Verify(progress);
    }

    [Fact]
    public async Task FullyFulfilledAlone_RecordsArm_AndDispatchesNothing()
    {
        var orderId = Guid.Parse("d0000000-0000-0000-0000-000000000002");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        // Drive fulfillment in isolation to completion so only the fulfillment arm
        // of the barrier lands; payment is never authorized.
        await app.SendAsync(new StartFulfillmentCommand(orderId, [Guid.NewGuid()]));
        await app.FireDueSchedulesAsync();

        Assert.Equal(0, CloseOrderCommandCount(app));
        Assert.Null((await GetRow(app, orderId))?.ClosedAt);

        var progress = await app.GetSagaProgress<OrderClosureSaga, OrderClosureProgress>(orderId);
        await Verify(progress);
    }

    [Fact]
    public async Task PaymentThenFulfillment_ClosesOrder()
    {
        var orderId = Guid.Parse("d0000000-0000-0000-0000-000000000003");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        // The natural pipeline order: submit drives payment authorization first, so
        // the payment arm lands while fulfillment is still scheduled.
        await app.SendAsync(new PlaceOrderCommand(orderId, "REF-001"));
        await app.SendAsync(new AddLineItemCommand(orderId, Guid.NewGuid(), "SKU-1", 1));
        await app.SendAsync(new AddLineItemCommand(orderId, Guid.NewGuid(), "SKU-2", 1));
        await app.SendAsync(new SubmitOrderCommand(orderId, Amount: 100m));
        await app.Drain();

        // One arm landed: the barrier dispatches nothing yet.
        Assert.Equal(0, CloseOrderCommandCount(app));

        // The terminal fulfillment fire raises OrderFullyFulfilled, tripping the join.
        await app.FireDueSchedulesAsync();
        await app.FireDueSchedulesAsync();

        await AssertClosedExactlyOnce(app, orderId);

        var progress = await app.GetSagaProgress<OrderClosureSaga, OrderClosureProgress>(orderId);
        await Verify(progress);
    }

    [Fact]
    public async Task FulfillmentThenPayment_ClosesOrder()
    {
        var orderId = Guid.Parse("d0000000-0000-0000-0000-000000000004");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        // The reverse arrival order, unreachable through the natural pipeline:
        // fulfillment completes first, so the fulfillment arm lands before payment.
        await app.SendAsync(new StartFulfillmentCommand(orderId, [Guid.NewGuid()]));
        await app.FireDueSchedulesAsync();

        // One arm landed: the barrier dispatches nothing yet.
        Assert.Equal(0, CloseOrderCommandCount(app));

        // Authorizing payment now lands the second arm and trips the same join.
        await app.SendAsync(new AuthorizePaymentCommand(orderId, Amount: 100m));
        await app.Drain();

        await AssertClosedExactlyOnce(app, orderId);
    }

    static async Task AssertClosedExactlyOnce(EdictTestApp app, Guid orderId)
    {
        Assert.Equal(1, CloseOrderCommandCount(app));
        Assert.Contains(
            app.Timeline.Entries,
            e => e.Kind == "Event" && e.Type == nameof(OrderClosedEvent));
        Assert.NotNull((await GetRow(app, orderId))?.ClosedAt);
    }

    static int CloseOrderCommandCount(EdictTestApp app) =>
        app.Timeline.Entries.Count(e =>
            e.Kind == "Command" && e.Type == nameof(CloseOrderCommand));

    static Task<OrderStatusRow?> GetRow(EdictTestApp app, Guid orderId) =>
        app.GetProjectionRow<OrderStatusRow>(
            tableName: "ordersbystatus",
            partitionKey: orderId.ToString(),
            rowKey: "status");
}
