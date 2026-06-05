using Edict.Contracts.Commands;
using Edict.Testing;

using Sample.Contracts.Cart.Commands;
using Sample.Domain.Cart.CommandHandlers;

using Xunit;

namespace Sample.Azure.Silo.Tests.Cart;

/// <summary>
/// Consumer reference for a state-aware Command Validator. Checking out a cart
/// with no items is rejected before <c>HandleAsync</c> runs: a
/// <c>CartCheckoutCommandValidator</c> reads the injected <c>CartState</c> and
/// rejects an empty basket, so the Order is never placed. Asserted through the
/// observable <see cref="EdictCommandResult"/> seam: an empty checkout is
/// <see cref="EdictCommandResult.Rejected"/> with the <c>empty_cart</c> reason,
/// while a checkout after a single state-only add is
/// <see cref="EdictCommandResult.Accepted"/>.
/// </summary>
public sealed class CartCheckoutEmptyRejectionTests
{
    [Fact]
    public async Task CheckingOutAnEmptyCartIsRejected()
    {
        var cartId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(CartCommandHandler).Assembly));

        // Act: check out a cart that has accumulated no items.
        var result = await app.SendAsync(new CheckoutCartCommand(cartId));

        // Assert: the validator gated the empty cart before the handler ran.
        var rejected = Assert.IsType<EdictCommandResult.Rejected>(result);
        var reason = Assert.Single(rejected.Reasons);
        Assert.Equal("empty_cart", reason.Code);
    }

    [Fact]
    public async Task CheckingOutAfterAnAddIsAccepted()
    {
        var cartId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(CartCommandHandler).Assembly));

        await app.SendAsync(new AddItemToCartCommand(cartId, "SKU-A"));

        // Act: check out the non-empty cart.
        var result = await app.SendAsync(new CheckoutCartCommand(cartId));

        // Assert: the validator passes a non-empty basket through to the handler.
        Assert.IsType<EdictCommandResult.Accepted>(result);
    }
}
