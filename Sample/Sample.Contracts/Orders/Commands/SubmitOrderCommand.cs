using Edict.Contracts.Commands;

namespace Sample.Contracts.Orders.Commands;

public sealed partial record SubmitOrderCommand(Guid OrderId, decimal Amount) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    /// <summary>Order total the OrderPayment saga forwards to the payment aggregate.</summary>
    public decimal Amount { get; init; } = Amount;
}
