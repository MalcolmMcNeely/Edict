using Edict.Contracts.Audit;
using Edict.Contracts.Sending;

using Sample.Contracts.Orders.Commands;

namespace Sample.Web.Components.Simulator;

/// <summary>
/// Default <see cref="IDeterministicOrderPlacer"/>. Three fixed line items at a
/// fixed sub-threshold amount; mints a fresh <c>OrderId</c> per call so each
/// "Fire one" press still produces a distinct trace, but the lifecycle shape
/// is identical every time.
/// </summary>
public sealed class FireOneOrderHelper : IDeterministicOrderPlacer
{
    // The sample has no auth, so the "Fire one" button stands in for an
    // authenticated operator clicking it: a chosen demo principal at the mock
    // edge, passed explicitly so EDICT023's origin-send gate stays satisfied.
    static readonly EdictPrincipal DemoOperator = EdictPrincipal.Of("demo-operator");

    const decimal FixedAmount = 100m;
    static readonly (string Sku, int Quantity)[] FixedLines =
    [
        ("SKU-A", 1),
        ("SKU-B", 2),
        ("SKU-C", 1),
    ];

    readonly IEdictSender _sender;
    readonly KnownOrdersRegistry _knownOrders;

    public FireOneOrderHelper(IEdictSender sender, KnownOrdersRegistry knownOrders)
    {
        _sender = sender;
        _knownOrders = knownOrders;
    }

    public async Task<Guid> FireOneAsync(CancellationToken cancellationToken = default)
    {
        var orderId = Guid.NewGuid();
        await _sender.SendAsync(new PlaceOrderCommand(orderId, "FIRE-ONE"), DemoOperator);
        _knownOrders.Register(orderId);

        foreach (var (sku, quantity) in FixedLines)
        {
            await _sender.SendAsync(new AddLineItemCommand(orderId, Guid.NewGuid(), sku, quantity), DemoOperator);
        }

        await _sender.SendAsync(new SubmitOrderCommand(orderId, FixedAmount), DemoOperator);
        return orderId;
    }
}
