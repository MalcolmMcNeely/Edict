using Azure.Data.Tables;

using Edict.Contracts.Events;
using Edict.Core.Grains;

namespace Sample.Silo.Orders;

public sealed partial class OrdersByStatusGrain : EdictTableProjectionBuilderGrain<OrderStatusRow>
{
    public OrdersByStatusGrain(TableServiceClient tableServiceClient)
        : base(tableServiceClient) { }

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
