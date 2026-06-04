using Edict.Contracts.Persistence;

namespace Sample.Domain.Cart.State;

/// <summary>
/// Framework-owned durable aggregate state for a shopping cart. Items
/// accumulate here across state-only Commands that raise no Event; Edict
/// commits this on every completing <c>HandleAsync</c>, so the accumulated
/// items survive to the later Command that acts on them. Persisted grain
/// state, so a frozen string-literal <c>[Alias]</c> survives a class rename;
/// <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Silo.Cart.CartState")]
public sealed class CartState : IEdictPersistedState
{
    [Id(0)]
    public List<string> Skus { get; set; } = [];

    [Id(1)]
    public bool CheckedOut { get; set; }
}
