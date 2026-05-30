using Edict.Contracts.Events;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using Sample.Contracts.Fulfillment.Domain;
using Sample.Contracts.Fulfillment.Events;
using Sample.Contracts.Fulfillment.Projections;
using Sample.Contracts.Orders.Events;

namespace Sample.Domain.Fulfillment.ProjectionBuilders;

/// <summary>
/// One row per <c>(orderId, lineItemId)</c> mapping the line's current
/// fulfillment state. Partition key is the OrderId so the Blazor Fulfillment
/// page can fetch all rows for an order; row key is the LineItemId.
/// </summary>
public sealed partial class LineItemFulfillmentTableProjectionBuilder : EdictTableProjectionBuilder<LineItemFulfillmentRow>
{
    public LineItemFulfillmentTableProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "lineitemfulfillment";

    protected override string GetRowKey(EdictEvent edictEvent) => edictEvent switch
    {
        LineItemAddedEvent added => added.LineItemId.ToString(),
        LineItemFulfilledEvent fulfilled => fulfilled.LineItemId.ToString(),
        _ => "",
    };

    public Task HandleAsync(LineItemAddedEvent edictEvent)
    {
        CurrentRow.LineItemId = edictEvent.LineItemId;
        CurrentRow.Status = LineItemFulfillmentStatus.Pending;
        return Task.CompletedTask;
    }

    public Task HandleAsync(LineItemFulfilledEvent edictEvent)
    {
        CurrentRow.LineItemId = edictEvent.LineItemId;
        CurrentRow.Status = LineItemFulfillmentStatus.Fulfilled;
        return Task.CompletedTask;
    }
}
