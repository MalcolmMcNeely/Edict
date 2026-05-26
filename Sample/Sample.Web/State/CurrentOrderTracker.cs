namespace Sample.Web.State;

/// <summary>
/// Demo-scope shared state for the hub's spotlighted <c>OrderId</c>. Written by
/// "Fire one order" and by row-click on the hub's orders table; never written
/// by the simulator. Registered as a singleton — fine for a single-user local
/// demo, intentionally not safe for multi-user production.
/// </summary>
public sealed class CurrentOrderTracker
{
    Guid? _orderId;

    public Guid? Current => _orderId;

    public void Set(Guid orderId) => _orderId = orderId;

    public void Clear() => _orderId = null;
}
