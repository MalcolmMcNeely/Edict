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
        Raise(new OrderSubmittedEvent(command.OrderId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> Handle(CancelOrderCommand command)
    {
        if (State.Status == OrderStatus.Submitted)
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("already_submitted", "Order has already been submitted.")]));

        State.Status = OrderStatus.Cancelled;
        Raise(new OrderCancelledEvent(command.OrderId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}
