using Edict.Core.Sagas;

using Sample.Contracts.Fulfillment.Events;
using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Payments.Events;

namespace Sample.Domain.Orders.Sagas;

/// <summary>
/// Cross-aggregate barrier Saga: an Order closes only once <em>both</em> payment
/// authorization and full fulfillment have landed, in either arrival order. This
/// is the accumulate-now-act-later shape (complementing the one-Command-per-Event
/// shapes of <c>OrderPaymentSaga</c> and <c>OrderFulfillmentSaga</c>): each arm
/// records a bool in <see cref="OrderClosureProgress"/> and dispatches nothing on
/// its own; the second arm to land trips the join, dispatches the single
/// <see cref="CloseOrderCommand"/>, and completes. The barrier lives entirely in
/// durable Progress, so it holds across reactivation and across whichever arm
/// arrives first.
/// </summary>
public partial class OrderClosureSaga : EdictSaga<OrderClosureProgress>
{
    Task HandleAsync(PaymentAuthorizedEvent edictEvent)
    {
        Progress.PaymentAuthorized = true;
        CloseIfBothArmsLanded(edictEvent.OrderId);
        return Task.CompletedTask;
    }

    Task HandleAsync(OrderFullyFulfilledEvent edictEvent)
    {
        Progress.FullyFulfilled = true;
        CloseIfBothArmsLanded(edictEvent.OrderId);
        return Task.CompletedTask;
    }

    void CloseIfBothArmsLanded(Guid orderId)
    {
        if (!Progress.PaymentAuthorized || !Progress.FullyFulfilled)
        {
            return;
        }

        Dispatch(new CloseOrderCommand(orderId));
        // The join fires once: both arms are terminal facts, so no later Event can
        // reopen the workflow. A genuinely-new Event after this dead-letters rather
        // than re-closing.
        Complete();
    }
}
