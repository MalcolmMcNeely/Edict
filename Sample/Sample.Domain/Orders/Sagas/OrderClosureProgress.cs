using Edict.Contracts.Persistence;

namespace Sample.Domain.Orders.Sagas;

/// <summary>
/// Durable progress for the OrderClosure barrier Saga: one bool per join arm.
/// The Saga dispatches nothing until both arms are set, so this accumulated
/// state is the whole point. Persisted inside the saga's idempotency envelope,
/// so a frozen string-literal <c>[Alias]</c> survives a class rename;
/// <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Silo.Orders.OrderClosureProgress")]
public sealed class OrderClosureProgress : IEdictPersistedState
{
    [Id(0)]
    public bool PaymentAuthorized { get; set; }

    [Id(1)]
    public bool FullyFulfilled { get; set; }
}
