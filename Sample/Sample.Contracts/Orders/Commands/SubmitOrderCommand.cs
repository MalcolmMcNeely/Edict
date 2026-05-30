using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Orders.Commands;

/// <param name="Amount">Order total the OrderPayment saga forwards to the payment aggregate.</param>
public sealed partial record SubmitOrderCommand(
    [property: EdictRouteKey] [property: EdictTelemeterized] Guid OrderId,
    decimal Amount) : EdictCommand;
