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

    protected override string GetRowKey(EdictEvent edictEvent) => "status";

    public Task Handle(OrderPlacedEvent edictEvent)
    {
        CurrentRow.Status = "Open";
        CurrentRow.PlacedAt = edictEvent.OccurredAt;
        return Task.CompletedTask;
    }

    public Task Handle(LineItemAddedEvent edictEvent)
    {
        CurrentRow.ItemCount++;
        return Task.CompletedTask;
    }

    public Task Handle(OrderSubmittedEvent edictEvent)
    {
        CurrentRow.Status = "Submitted";
        CurrentRow.SubmittedAt = edictEvent.OccurredAt;
        return Task.CompletedTask;
    }

    public Task Handle(OrderCancelledEvent edictEvent)
    {
        CurrentRow.Status = "Cancelled";
        return Task.CompletedTask;
    }

    public Task Handle(PaymentAuthorizedEvent edictEvent)
    {
        CurrentRow.AuthorizedAt = edictEvent.OccurredAt;
        return Task.CompletedTask;
    }

    public Task Handle(OrderFullyFulfilledEvent edictEvent)
    {
        CurrentRow.FulfilledAt = edictEvent.OccurredAt;
        return Task.CompletedTask;
    }

    public Task Handle(OrderShippedEvent edictEvent)
    {
        CurrentRow.Status = "Shipped";
        CurrentRow.ShippedAt = edictEvent.OccurredAt;
        return Task.CompletedTask;
    }
}
