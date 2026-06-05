using Edict.Contracts.Commands;
using Edict.Core.Commands;

using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Orders.Domain;
using Sample.Contracts.Orders.Events;
using Sample.Domain.Orders.State;

namespace Sample.Domain.Orders.CommandHandlers;

// End-to-end demo of the inline-drain path: the handler mutates framework-owned
// State and raises events; Edict commits {State, Outbox} in one write and the
// inline FIFO drain publishes. No volatile aggregate fields.
public partial class OrderCommandHandler : EdictCommandHandler<OrderState>
{
    // Per-line tally for a bridged cart order, kept below the payment decline
    // threshold so a checked-out cart follows the happy path.
    const decimal LineItemAmount = 25m;

    Task<EdictCommandResult> HandleAsync(PlaceOrderCommand command)
    {
        State.Status = OrderStatus.Open;
        State.Items.Clear();
        Raise(new OrderPlacedEvent(command.OrderId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    // The Cart to Order bridge Saga's single dispatch lands here: place and submit
    // the whole basket atomically so the Order flows straight into the existing
    // payment and fulfillment workflow. Only OrderSubmitted sets the projected
    // status; a LineItemAdded per basket entry carries the line items through to
    // OrderConfirmed for fulfillment.
    Task<EdictCommandResult> HandleAsync(PlaceOrderFromCartCommand command)
    {
        State.Status = OrderStatus.Submitted;
        State.Items.Clear();
        foreach (var sku in command.Skus)
        {
            var lineItemId = Guid.NewGuid();
            State.Items.Add(new OrderLine { LineItemId = lineItemId, Sku = sku, Quantity = 1 });
            Raise(new LineItemAddedEvent(command.OrderId, lineItemId, sku, 1));
        }

        Raise(new OrderSubmittedEvent(command.OrderId, command.Skus.Count * LineItemAmount));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    Task<EdictCommandResult> HandleAsync(AddLineItemCommand command)
    {
        if (State.Status != OrderStatus.Open)
        {
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("order_not_open", "Order is not open for modifications.")]));
        }

        State.Items.Add(new OrderLine { LineItemId = command.LineItemId, Sku = command.Sku, Quantity = command.Quantity });
        Raise(new LineItemAddedEvent(command.OrderId, command.LineItemId, command.Sku, command.Quantity));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    Task<EdictCommandResult> HandleAsync(SubmitOrderCommand command)
    {
        if (State.Items.Count == 0)
        {
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("no_items", "Order has no line items.")]));
        }

        State.Status = OrderStatus.Submitted;
        Raise(new OrderSubmittedEvent(command.OrderId, command.Amount));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    // Driven by the OrderPayment saga on PaymentAuthorized — the happy-path
    // terminal transition.
    Task<EdictCommandResult> HandleAsync(ConfirmOrderCommand command)
    {
        if (State.Status == OrderStatus.Cancelled)
        {
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("order_cancelled", "Order has been cancelled.")]));
        }

        // Closed is terminal. The OrderClosure barrier Saga and the OrderPayment
        // saga both react to PaymentAuthorized and target this grain, so a confirm
        // can lose the race to a close; once closed, confirm must not reopen it.
        if (State.Status == OrderStatus.Closed)
        {
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("order_closed", "Order has been closed.")]));
        }

        State.Status = OrderStatus.Confirmed;
        // Carry the snapshot of line item ids so the OrderFulfillment saga can
        // hand them to FulfillmentCommandHandler without re-fetching the order.
        var lineItemIds = State.Items.Select(i => i.LineItemId).ToArray();
        Raise(new OrderConfirmedEvent(command.OrderId, lineItemIds));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    // Driven by the OrderFulfillment saga on OrderFullyFulfilled. The OrderClosure
    // barrier saga fires on the same Event, so a close can win the race and land
    // first: a closed order still records its shipment (Closed stays the terminal
    // status, shipping does not reopen it), so the demo always shows both. Only an
    // order that never confirmed rejects.
    Task<EdictCommandResult> HandleAsync(MarkOrderShippedCommand command)
    {
        if (State.Status != OrderStatus.Confirmed && State.Status != OrderStatus.Closed)
        {
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("order_not_confirmed", "Order is not confirmed.")]));
        }

        if (State.Status == OrderStatus.Confirmed)
        {
            State.Status = OrderStatus.Shipped;
        }

        Raise(new OrderShippedEvent(command.OrderId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    // Driven by the OrderClosure barrier saga once both payment authorization and
    // full fulfillment have landed. Permissive on purpose: it races ConfirmOrder and
    // MarkOrderShipped on the same trigger Events, so whichever status it finds it
    // closes. Closed is the sink state; only a cancelled order rejects.
    Task<EdictCommandResult> HandleAsync(CloseOrderCommand command)
    {
        if (State.Status == OrderStatus.Cancelled)
        {
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("order_cancelled", "Order has been cancelled.")]));
        }

        State.Status = OrderStatus.Closed;
        Raise(new OrderClosedEvent(command.OrderId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    // A submitted order stays cancellable — that is exactly the OrderPayment
    // saga's compensation branch (PaymentDeclined → CancelOrder). Only a
    // confirmed order is terminal and rejects cancellation.
    Task<EdictCommandResult> HandleAsync(CancelOrderCommand command)
    {
        if (State.Status == OrderStatus.Confirmed)
        {
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("already_confirmed", "Order has already been confirmed.")]));
        }

        State.Status = OrderStatus.Cancelled;
        Raise(new OrderCancelledEvent(command.OrderId, command.Reason));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}
