using Edict.Contracts.Events;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using Sample.Contracts.Orders.Events;
using Sample.Contracts.Payments.Projections;

namespace Sample.Silo.Payments;

/// <summary>
/// Projects the OrderPayment saga's terminal order events into a queryable
/// outcome, so the workflow's happy path and compensation branch are
/// observable through a read endpoint. Kept separate from the status
/// projection so each projection has a single, stable responsibility.
/// </summary>
public sealed partial class OrderOutcomeProjectionBuilder : EdictTableProjectionBuilder<OrderOutcomeRow>
{
    public OrderOutcomeProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "orderoutcome";

    protected override string GetRowKey(EdictEvent evt) => "outcome";

    public Task Handle(OrderConfirmedEvent evt)
    {
        CurrentRow.Outcome = "Confirmed";
        return Task.CompletedTask;
    }

    public Task Handle(OrderCancelledEvent evt)
    {
        CurrentRow.Outcome = "Cancelled";
        return Task.CompletedTask;
    }
}
