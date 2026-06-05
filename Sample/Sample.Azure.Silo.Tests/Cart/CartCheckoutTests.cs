using Edict.Testing;

using Sample.Contracts.Cart.Commands;
using Sample.Contracts.Cart.Events;
using Sample.Contracts.Cart.Projections;
using Sample.Domain.Cart.CommandHandlers;

using Xunit;

namespace Sample.Azure.Silo.Tests.Cart;

/// <summary>
/// Consumer reference for the accumulate-now-act-later pattern. Two
/// <c>AddItemToCart</c> Commands mutate <c>State</c> and raise no Event; the
/// framework persists each completing <c>HandleAsync</c>, so the accumulated
/// items survive to the later <c>CheckoutCart</c> Command, which raises one
/// <c>CartCheckedOutEvent</c> derived from the accumulated state. Everything is
/// asserted through observable surfaces: the cart-scoped slice of the
/// <see cref="EdictTestApp.Timeline"/> shows the three Commands raise exactly one
/// Event, and the projection read-model row proves the downstream
/// <c>CheckedOutCartProjectionBuilder</c> was driven by that Event. Neither
/// assertion reaches into private grain <c>State</c>. The bridge Saga's
/// downstream Order cascade (covered by <see cref="CartToOrderBridgeTests"/>) is
/// scoped out: its multi-saga ordering is chaos-sensitive, so a full-timeline
/// snapshot here would be non-deterministic.
/// </summary>
public sealed class CartCheckoutTests
{
    [Fact]
    public async Task CheckoutCart_RaisesEventFromItemsAccumulatedByStateOnlyCommands_AndDrivesProjection()
    {
        var cartId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(CartCommandHandler).Assembly));

        // Two state-only Commands: each mutates State and raises no Event.
        await app.SendAsync(new AddItemToCartCommand(cartId, "SKU-A"));
        await app.SendAsync(new AddItemToCartCommand(cartId, "SKU-B"));
        // Acts on the accumulated state: raises one Event carrying the basket.
        await app.SendAsync(new CheckoutCartCommand(cartId));
        await app.Drain();

        var cartTimeline = app.Timeline.Entries
            .Where(entry => entry.Type is nameof(AddItemToCartCommand)
                or nameof(CheckoutCartCommand)
                or nameof(CartCheckedOutEvent))
            .ToList();

        var row = await app.GetProjectionRow<CheckedOutCartRow>(
            tableName: "checkedoutcarts",
            partitionKey: cartId.ToString(),
            rowKey: "cart");

        await Verify(new { CartTimeline = cartTimeline, ProjectionRow = row });
    }
}
