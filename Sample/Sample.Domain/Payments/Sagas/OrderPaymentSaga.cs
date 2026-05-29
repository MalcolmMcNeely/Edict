using Edict.Core.Sagas;

using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Orders.Events;
using Sample.Contracts.Payments.Commands;
using Sample.Contracts.Payments.Events;

namespace Sample.Domain.Payments.Sagas;

/// <summary>
/// Coordinates the order→payment workflow across two aggregates,
/// keyed throughout by the order's Guid (cross-domain re-keying):
/// <list type="bullet">
///   <item><c>OrderSubmitted → AuthorizePayment</c></item>
///   <item><c>PaymentAuthorized → ConfirmOrder</c> (happy path)</item>
///   <item><c>PaymentDeclined → CancelOrder</c> (compensation)</item>
/// </list>
/// Each handler issues exactly one Command via <see cref="EdictSaga{TProgress}.Dispatch"/>;
/// the dedup ring, durable <see cref="EdictSaga{TProgress}.Progress"/>, and the
/// SendCommand effect commit atomically in the one grain-document write.
/// </summary>
public partial class OrderPaymentSaga : EdictSaga<OrderPaymentProgress>
{
    public Task Handle(OrderSubmittedEvent edictEvent)
    {
        Progress.Stage = OrderPaymentStage.PaymentRequested;
        Dispatch(new AuthorizePaymentCommand(edictEvent.OrderId, edictEvent.Amount));
        return Task.CompletedTask;
    }

    public Task Handle(PaymentAuthorizedEvent edictEvent)
    {
        Progress.Stage = OrderPaymentStage.Confirmed;
        Dispatch(new ConfirmOrderCommand(edictEvent.OrderId));
        return Task.CompletedTask;
    }

    public Task Handle(PaymentDeclinedEvent edictEvent)
    {
        Progress.Stage = OrderPaymentStage.Compensated;
        Dispatch(new CancelOrderCommand(edictEvent.OrderId, "payment_declined"));
        return Task.CompletedTask;
    }
}
