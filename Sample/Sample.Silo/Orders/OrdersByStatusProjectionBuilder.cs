using Edict.Contracts.Events;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using Sample.Contracts.Orders.Events;
using Sample.Contracts.Orders.Projections;

namespace Sample.Silo.Orders;

public sealed partial class OrdersByStatusProjectionBuilder : EdictTableProjectionBuilder<OrderStatusRow>
{
    public OrdersByStatusProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "ordersbystatus";

    protected override string GetRowKey(EdictEvent evt) => "status";

    public Task Handle(OrderPlacedEvent evt)
    {
        CurrentRow.Status = "Open";
        CurrentRow.ItemCount = 0;
        return Task.CompletedTask;
    }

    public Task Handle(LineItemAddedEvent evt)
    {
        CurrentRow.ItemCount++;
        return Task.CompletedTask;
    }

    public Task Handle(OrderSubmittedEvent evt)
    {
        CurrentRow.Status = "Submitted";
        return Task.CompletedTask;
    }

    public Task Handle(OrderCancelledEvent evt)
    {
        CurrentRow.Status = "Cancelled";
        return Task.CompletedTask;
    }
}
