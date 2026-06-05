using Edict.Contracts.Persistence;

namespace Sample.Domain.Settlement.State;

/// <summary>
/// Framework-owned durable aggregate state for a gateway settlement. Persisted
/// grain state, so a frozen string-literal <c>[Alias]</c> survives a class
/// rename; <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Silo.Settlement.GatewaySettlementState")]
public sealed class GatewaySettlementState : IEdictPersistedState
{
    [Id(0)]
    public Guid PaymentId { get; set; }

    [Id(1)]
    public bool Settled { get; set; }

    [Id(2)]
    public bool Abandoned { get; set; }
}
