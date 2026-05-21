using Edict.Contracts.Persistence;

namespace Sample.Silo.Fulfillment.Sagas;

/// <summary>Stage the OrderFulfillment saga has durably reached.</summary>
public enum OrderFulfillmentStage
{
    Started,
    FulfillmentRequested,
    Shipped,
}

/// <summary>
/// Durable progress for the OrderFulfillment saga. Persisted inside the saga's
/// idempotency envelope, so a frozen string-literal <c>[Alias]</c> survives a
/// class rename; <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Silo.Fulfillment.OrderFulfillmentProgress")]
public sealed class OrderFulfillmentProgress : IEdictPersistedState
{
    [Id(0)]
    public OrderFulfillmentStage Stage { get; set; } = OrderFulfillmentStage.Started;
}
