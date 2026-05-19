using Edict.Contracts.Commands;
using Edict.Core.Commands;

namespace Sample.Silo.Orders;

// End-to-end demo of the inline-drain path: the handler mutates framework-owned
// State and raises events; Edict commits {State, Outbox} in one write and the
// inline FIFO drain publishes (ADR 0018). No volatile aggregate fields.
public partial class OrderCommandHandler : EdictCommandHandler<OrderState>
{
    public Task<EdictCommandResult> Handle(PlaceOrderCommand command)
    {
        State.Status = OrderStatus.Open;
        State.Items.Clear();
        Raise(new OrderPlacedEvent(command.OrderId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> Handle(AddLineItemCommand command)
    {
        if (State.Status != OrderStatus.Open)
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("order_not_open", "Order is not open for modifications.")]));

        State.Items.Add(new OrderLine { Sku = command.Sku, Quantity = command.Quantity });
        Raise(new LineItemAddedEvent(command.OrderId, command.Sku, command.Quantity));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> Handle(SubmitOrderCommand command)
    {
        if (State.Items.Count == 0)
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("no_items", "Order has no line items.")]));

        State.Status = OrderStatus.Submitted;
        Raise(new OrderSubmittedEvent(command.OrderId, command.Amount));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    // Driven by the OrderPayment saga on PaymentAuthorized — the happy-path
    // terminal transition.
    public Task<EdictCommandResult> Handle(ConfirmOrderCommand command)
    {
        if (State.Status == OrderStatus.Cancelled)
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("order_cancelled", "Order has been cancelled.")]));

        State.Status = OrderStatus.Confirmed;
        Raise(new OrderConfirmedEvent(command.OrderId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    // A submitted order stays cancellable — that is exactly the OrderPayment
    // saga's compensation branch (PaymentDeclined → CancelOrder). Only a
    // confirmed order is terminal and rejects cancellation.
    public Task<EdictCommandResult> Handle(CancelOrderCommand command)
    {
        if (State.Status == OrderStatus.Confirmed)
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("already_confirmed", "Order has already been confirmed.")]));

        State.Status = OrderStatus.Cancelled;
        Raise(new OrderCancelledEvent(command.OrderId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}
