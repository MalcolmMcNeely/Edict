using Edict.Core.Sagas;

using Sample.Contracts.Cart.Events;
using Sample.Contracts.Orders.Commands;

namespace Sample.Domain.Cart.Sagas;

/// <summary>
/// Bridges the Cart and Order aggregates without coupling them: it reacts to the
/// one <c>CartCheckedOutEvent</c> and dispatches a single
/// <see cref="PlaceOrderFromCartCommand"/> carrying the whole basket, then
/// completes. The placement keys the new Order by the cart's id, so the two
/// aggregates stay independent while the demo can follow one correlation through
/// to payment and fulfillment. The single-Command-per-Event saga contract holds:
/// the basket rides one Command, not a Command per item.
/// </summary>
public partial class CartToOrderSaga : EdictSaga<CartToOrderProgress>
{
    Task HandleAsync(CartCheckedOutEvent edictEvent)
    {
        Progress.OrderId = edictEvent.CartId;
        Dispatch(new PlaceOrderFromCartCommand(edictEvent.CartId, edictEvent.Skus));
        // A cart checks out once; no later Event can reopen the bridge, so a
        // genuinely-new Event after this dead-letters rather than re-placing.
        Complete();
        return Task.CompletedTask;
    }
}
