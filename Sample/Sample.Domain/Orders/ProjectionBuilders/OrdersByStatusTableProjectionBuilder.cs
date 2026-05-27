using Edict.Contracts.Events;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using Sample.Contracts.Fulfillment.Events;
using Sample.Contracts.Orders.Events;
using Sample.Contracts.Orders.Projections;
using Sample.Contracts.Payments.Events;

namespace Sample.Domain.Orders.ProjectionBuilders;

public sealed partial class OrdersByStatusTableProjectionBuilder : EdictTableProjectionBuilder<OrderStatusRow>
{
    public OrdersByStatusTableProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "ordersbystatus";

    protected override string GetRowKey(EdictEvent evt) => "status";

    public Task Handle(OrderPlacedEvent evt)
    {
        CurrentRow.Status = "Open";
        CurrentRow.PlacedAt = evt.OccurredAt;
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
        CurrentRow.SubmittedAt = evt.OccurredAt;
        return Task.CompletedTask;
    }

    public Task Handle(OrderCancelledEvent evt)
    {
        CurrentRow.Status = "Cancelled";
        return Task.CompletedTask;
    }

    public Task Handle(PaymentAuthorizedEvent evt)
    {
        CurrentRow.AuthorizedAt = evt.OccurredAt;
        return Task.CompletedTask;
    }

    public Task Handle(OrderFullyFulfilledEvent evt)
    {
        CurrentRow.FulfilledAt = evt.OccurredAt;
        return Task.CompletedTask;
    }

    public Task Handle(OrderShippedEvent evt)
    {
        CurrentRow.Status = "Shipped";
        CurrentRow.ShippedAt = evt.OccurredAt;
        return Task.CompletedTask;
    }
}
