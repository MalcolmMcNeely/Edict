using System.Collections.Concurrent;

namespace Sample.Web.Components.Simulator;

/// <summary>
/// Demo-prop registry of order ids placed in this web process — written by the
/// simulator and the deterministic "fire one" helper, read by the hub to drive
/// its orders table. Not a framework primitive: <c>OrderStatusRow</c>'s table
/// partitions by <c>OrderId</c>, so the hub needs a list of partitions to
/// point-get from. A production consumer would replace this with a fleet-wide
/// projection scan.
/// </summary>
public sealed class KnownOrdersRegistry
{
    readonly ConcurrentDictionary<Guid, byte> _orders = new();

    public void Register(Guid orderId) => _orders.TryAdd(orderId, 0);

    public IReadOnlyCollection<Guid> Snapshot() => _orders.Keys.ToArray();
}
