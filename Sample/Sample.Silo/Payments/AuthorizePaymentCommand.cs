using Edict.Contracts.Commands;

using MessagePack;

namespace Sample.Silo.Payments;

[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record AuthorizePaymentCommand(Guid OrderId, decimal Amount) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public decimal Amount { get; init; } = Amount;
}
