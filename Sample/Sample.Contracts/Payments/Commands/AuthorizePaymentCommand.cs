using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

namespace Sample.Contracts.Payments.Commands;

public sealed partial record AuthorizePaymentCommand(Guid OrderId, decimal Amount) : EdictCommand
{
    [EdictRouteKey]
    [EdictTelemeterized]
    public Guid OrderId { get; init; } = OrderId;

    public decimal Amount { get; init; } = Amount;
}
