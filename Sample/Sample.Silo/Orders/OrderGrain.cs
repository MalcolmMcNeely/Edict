using Edict.Contracts.Results;
using Edict.Core.Commands;

namespace Sample.Silo.Orders;

public partial class OrderGrain : EdictCommandHandlerGrain
{
    private enum OrderStatus { Open, Submitted, Cancelled }

    private OrderStatus _status = OrderStatus.Open;
    private readonly List<(string Sku, int Quantity)> _items = [];

    public Task<EdictCommandResult> Handle(PlaceOrderCommand command)
    {
        _status = OrderStatus.Open;
        _items.Clear();
        Raise(new OrderPlacedEvent(command.OrderId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> Handle(AddLineItemCommand command)
    {
        if (_status != OrderStatus.Open)
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("order_not_open", "Order is not open for modifications.")]));

        _items.Add((command.Sku, command.Quantity));
        Raise(new LineItemAddedEvent(command.OrderId, command.Sku, command.Quantity));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> Handle(SubmitOrderCommand command)
    {
        if (_items.Count == 0)
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("no_items", "Order has no line items.")]));

        _status = OrderStatus.Submitted;
        Raise(new OrderSubmittedEvent(command.OrderId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> Handle(CancelOrderCommand command)
    {
        if (_status == OrderStatus.Submitted)
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("already_submitted", "Order has already been submitted.")]));

        _status = OrderStatus.Cancelled;
        Raise(new OrderCancelledEvent(command.OrderId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}
