using Edict.Contracts.Events;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using Sample.Silo.Orders;

namespace Sample.Silo.Payments;

/// <summary>Read model for the terminal outcome of the OrderPayment workflow.</summary>
public sealed class OrderOutcomeRow
{
    public string Outcome { get; set; } = "Pending";
}

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
