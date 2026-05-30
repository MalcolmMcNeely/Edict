using Edict.Core.Sagas;

using Sample.Contracts.Fulfillment.Commands;
using Sample.Contracts.Fulfillment.Events;
using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Orders.Events;

namespace Sample.Domain.Fulfillment.Sagas;

/// <summary>
/// Coordinates the order→fulfillment workflow across two aggregates, keyed
/// throughout by the order's Guid (cross-domain re-keying). The
/// workflow-handoff Saga shape (complementing the compensation shape of
/// <c>OrderPaymentSaga</c>): one event in, one command out per handler.
/// <list type="bullet">
///   <item><c>OrderConfirmed → StartFulfillment</c></item>
///   <item><c>OrderFullyFulfilled → MarkOrderShipped</c></item>
/// </list>
/// </summary>
public partial class OrderFulfillmentSaga : EdictSaga<OrderFulfillmentProgress>
{
    public Task HandleAsync(OrderConfirmedEvent edictEvent)
    {
        Progress.Stage = OrderFulfillmentStage.FulfillmentRequested;
        Dispatch(new StartFulfillmentCommand(edictEvent.OrderId, edictEvent.LineItemIds));
        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderFullyFulfilledEvent edictEvent)
    {
        Progress.Stage = OrderFulfillmentStage.Shipped;
        Dispatch(new MarkOrderShippedCommand(edictEvent.OrderId));
        return Task.CompletedTask;
    }
}
