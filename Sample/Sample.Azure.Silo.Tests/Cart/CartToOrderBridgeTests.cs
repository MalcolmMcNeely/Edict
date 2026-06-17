using Edict.Testing;

using Sample.Contracts.Cart.Commands;
using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Orders.Projections;
using Sample.Domain.Cart.CommandHandlers;

using Xunit;

namespace Sample.Azure.Silo.Tests.Cart;

/// <summary>
/// Consumer reference for the Cart to Order bridge Saga. A cart accumulates
/// items through state-only Commands, then checks out raising one
/// <c>CartCheckedOutEvent</c>; the bridge Saga reacts to that Event and
/// dispatches exactly one order-placement Command carrying the whole basket,
/// keeping Cart and Order as independent aggregates. The placed Order then flows
/// through the existing payment workflow. Everything is asserted through
/// observable surfaces: the dispatched Command on the
/// <see cref="EdictTestApp.Timeline"/> and the order's read-model row, never the
/// saga's private <c>Progress</c>.
/// </summary>
public sealed class CartToOrderBridgeTests
{
    [Fact]
    public async Task CheckoutCart_BridgeSagaPlacesOrderCarryingTheBasket_AndOrderFlowsThroughPayment()
    {
        var cartId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(CartCommandHandler).Assembly));

        // Build a basket with state-only Commands, then check out.
        await app.SendAsync(new AddItemToCartCommand(cartId, "SKU-A"));
        await app.SendAsync(new AddItemToCartCommand(cartId, "SKU-B"));
        await app.SendAsync(new CheckoutCartCommand(cartId));
        await app.Drain();

        // The bridge reacted to the checkout Event and dispatched exactly one
        // order-placement Command carrying the whole basket.
        var placement = app.Timeline.Entries
            .Single(entry => entry.Kind == "Command" && entry.Type == nameof(PlaceOrderFromCartCommand));

        // The bridge keys the Order by the cart's id, so the placed Order's
        // read-model row (driven through to payment authorization) is here.
        var orderStatus = await app.GetProjectionRow<OrderStatusRow>(
            tableName: "ordersbystatus",
            partitionKey: cartId.ToString("N"),
            rowKey: "status");

        await Verify(new { Placement = placement, OrderStatus = orderStatus });
    }
}
