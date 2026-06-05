using Edict.Contracts.Commands;
using Edict.Core.Commands;

using Sample.Contracts.Cart.Commands;
using Sample.Contracts.Cart.Events;
using Sample.Domain.Cart.State;

namespace Sample.Domain.Cart.CommandHandlers;

// Demonstrates accumulate-now-act-later. AddItemToCart mutates State and raises
// no Event, so building up a cart makes no downstream noise. Edict persists the
// mutation on every completing HandleAsync, so the accumulated items survive to
// the later CheckoutCart Command. CheckoutCart reads that accumulated state and
// raises one Event carrying the whole basket, which downstream consumers react
// to.
public partial class CartCommandHandler : EdictCommandHandler<CartState>
{
    Task<EdictCommandResult> HandleAsync(AddItemToCartCommand command)
    {
        if (State.CheckedOut)
        {
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("cart_checked_out", "Cart has already been checked out.")]));
        }

        State.Skus.Add(command.Sku);
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    Task<EdictCommandResult> HandleAsync(CheckoutCartCommand command)
    {
        if (State.Skus.Count == 0)
        {
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("empty_cart", "Cart has no items to check out.")]));
        }

        if (State.CheckedOut)
        {
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("already_checked_out", "Cart has already been checked out.")]));
        }

        State.CheckedOut = true;
        Raise(new CartCheckedOutEvent(command.CartId, State.Skus.Count, State.Skus.ToArray()));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}
