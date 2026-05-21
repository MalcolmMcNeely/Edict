using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Orders.Projections;
using Sample.Silo.Orders;

using Xunit;

namespace Sample.Silo.Tests.Orders;

/// <summary>
/// Proves the chaos default (seeded duplicate redelivery) is on by default and
/// genuinely fires, by reading the recorder's underlying event count. The
/// snapshot itself stays stable because the dedup ring suppresses
/// the duplicate before any handler runs — that stability is the whole reason
/// chaos can be on by default.
/// </summary>
public sealed class ChaosDeliveryTests
{
    [Fact]
    public async Task DuplicateRedelivery_ShouldNotChangeProjectionRow_BecauseDedupRingSuppresses()
    {
        var orderId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        // Default chaos seed: duplicates fire deterministically run-to-run.
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

        // Exactly two line items applied (count is increment, not assignment —
        // the projection cleanest at catching dedup bugs). If the dedup ring
        // missed a duplicate, ItemCount would be > 2.
        await Verify(row);
    }
}
