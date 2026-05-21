using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Orders.Projections;
using Sample.Silo.Orders.CommandHandlers;
using Sample.Silo.Payments.Sagas;

using Xunit;

namespace Sample.Silo.Tests.Orders;

/// <summary>
/// End-to-end proof that the in-process harness applies bounded reorder chaos
/// on every test run. ADR 0015 line 7 documents that a redelivered older event
/// can land behind a newer one of the same aggregate — consumers must be
/// reorder-tolerant; the harness proves the property is enforced.
/// <para>
/// The fragile assertion targets <c>OrdersByStatusTableProjectionBuilder</c>:
/// its <c>Handle(OrderPlacedEvent)</c> re-initialises <c>ItemCount = 0</c>.
/// Strict order would land OrderPlaced first and the final
/// row would carry <c>ItemCount = 1</c>; reorder lands OrderPlaced behind the
/// later events, the reset wins last, the row carries <c>ItemCount = 0</c>.
/// Without the reorder slice the assertion would go red; with it, chaos fires
/// deterministically against the fixed seed and the assertion is green.
/// </para>
/// <para>
/// The control assertion targets <see cref="OrderPaymentSaga"/>: it acts only
/// on the latest event for the order, so reorder cannot perturb its terminal
/// stage. Stays green — chaos is not a false-positive generator.
/// </para>
/// </summary>
public sealed class ReorderChaosContractTests
{
    [Fact]
    public async Task ReorderFragileProjection_SurfacesOrderDependence_UnderChaos()
    {
        var orderId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Send(new AddLineItemCommand(orderId, Guid.NewGuid(), "SKU-1", 1));
        await app.Drain();

        var row = await app.GetProjectionRow<OrderStatusRow>(
            tableName: "ordersbystatus",
            partitionKey: orderId.ToString(),
            rowKey: "status");

        // Strict order would leave ItemCount = 1; reorder lands OrderPlaced
        // after LineItemAdded, so OrderPlaced's ItemCount = 0 reset wins last.
        Assert.NotNull(row);
        Assert.Equal(0, row.ItemCount);
    }

    [Fact]
    public async Task ReorderTolerantSaga_StaysOnHappyBranch_UnderChaos()
    {
        var orderId = Guid.Parse("77777777-7777-7777-7777-777777777777");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Send(new AddLineItemCommand(orderId, Guid.NewGuid(), "SKU-1", 1));
        await app.Send(new SubmitOrderCommand(orderId, Amount: 100m));
        await app.Drain();

        var progress = await app.GetSagaProgress<OrderPaymentSaga, OrderPaymentProgress>(orderId);

        Assert.Equal(OrderPaymentStage.Confirmed, progress.Stage);
    }
}
