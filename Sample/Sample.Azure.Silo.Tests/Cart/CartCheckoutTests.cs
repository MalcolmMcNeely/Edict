using Edict.Testing;

using Sample.Contracts.Cart.Commands;
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
/// asserted through observable surfaces: the <see cref="EdictTestApp.Timeline"/>
/// shows the Commands and the raised Event, and the projection read-model row
/// proves the downstream <c>CheckedOutCartTableProjectionBuilder</c> was driven
/// by that Event. Neither assertion reaches into private grain <c>State</c>.
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

        var row = await app.GetProjectionRow<CheckedOutCartRow>(
            tableName: "checkedoutcarts",
            partitionKey: cartId.ToString(),
            rowKey: "cart");

        await Verify(new { app.Timeline, ProjectionRow = row });
    }
}
