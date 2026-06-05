using Edict.Contracts.Persistence;

namespace Sample.Domain.Cart.Sagas;

/// <summary>
/// Durable progress for the Cart to Order bridge Saga. Persisted inside the
/// saga's idempotency envelope, so a frozen string-literal <c>[Alias]</c>
/// survives a class rename; <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Silo.Cart.CartToOrderProgress")]
public sealed class CartToOrderProgress : IEdictPersistedState
{
    [Id(0)]
    public Guid OrderId { get; set; }
}
