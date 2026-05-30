using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Orders.Events;

/// <param name="Amount">Order total the OrderPayment saga forwards to <c>AuthorizePaymentCommand</c>.</param>
[EdictStream("Orders")]
public sealed partial record OrderSubmittedEvent(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid OrderId,
    decimal Amount) : EdictEvent;
