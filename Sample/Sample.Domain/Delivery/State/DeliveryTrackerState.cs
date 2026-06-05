using Edict.Contracts.Persistence;

namespace Sample.Domain.Delivery.State;

/// <summary>
/// Framework-owned durable aggregate state for a delivery. Persisted grain state,
/// so a frozen string-literal <c>[Alias]</c> survives a class rename;
/// <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Silo.Delivery.DeliveryTrackerState")]
public sealed class DeliveryTrackerState : IEdictPersistedState
{
    [Id(0)]
    public Guid OrderId { get; set; }

    [Id(1)]
    public int EtaDaysRemaining { get; set; }
}
