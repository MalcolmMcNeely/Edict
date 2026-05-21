namespace Sample.Web.State;

/// <summary>
/// Demo-scope shared state for the most recently placed order id, so the
/// Fulfillment page can render its live rows without asking the user to copy
/// the OrderId from the Orders page. Registered as a singleton — fine for a
/// single-user local demo, intentionally not safe for multi-user production.
/// </summary>
public sealed class CurrentOrderTracker
{
    Guid? _orderId;

    public Guid? Current => _orderId;

    public void Set(Guid orderId) => _orderId = orderId;
}
