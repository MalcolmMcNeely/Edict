using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Orders.Events;

[EdictStream("Orders")]
public sealed partial record OrderSubmittedEvent(Guid OrderId, decimal Amount) : EdictEvent
{
    [EdictRouteKey]
    [EdictTelemeterized]
    public Guid OrderId { get; init; } = OrderId;

    /// <summary>Order total the OrderPayment saga forwards to <c>AuthorizePaymentCommand</c>.</summary>
    public decimal Amount { get; init; } = Amount;
}
