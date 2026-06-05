using Edict.Contracts.Persistence;

namespace Sample.Domain.Settlement.Sagas;

/// <summary>
/// Durable progress for the gateway settlement saga. Persisted inside the saga's
/// idempotency envelope, so a frozen string-literal <c>[Alias]</c> survives a
/// class rename; <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Silo.Settlement.GatewaySettlementProgress")]
public sealed class GatewaySettlementProgress : IEdictPersistedState
{
    [Id(0)]
    public Guid PaymentId { get; set; }

    [Id(1)]
    public int Polls { get; set; }
}
