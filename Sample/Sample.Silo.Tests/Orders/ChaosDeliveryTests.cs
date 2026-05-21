using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Orders.Projections;
using Sample.Silo.Orders;

using Xunit;

namespace Sample.Silo.Tests.Orders;

public sealed class ChaosDeliveryTests
{
    [Fact]
    public async Task DuplicateRedelivery_ShouldNotChangeProjectionRow_BecauseDedupRingSuppresses()
    {
        var orderId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId));
        await app.Send(new AddLineItemCommand(orderId, "SKU-1", 1));
        await app.Send(new AddLineItemCommand(orderId, "SKU-2", 2));
        await app.Drain();

        var row = await app.GetProjectionRow<OrderStatusRow>(
            tableName: "ordersbystatus",
            partitionKey: orderId.ToString(),
            rowKey: "status");

        // The projection counts via increment, so a missed dedup would push
        // ItemCount above 2 — the snapshot fixes it at 2.
        await Verify(row);
    }
}
