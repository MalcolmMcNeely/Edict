using Edict.Contracts.Persistence;

namespace Sample.Contracts.Cart.Projections;

[GenerateSerializer]
[Alias("Sample.Contracts.Cart.Projections.CheckedOutCartRow")]
public sealed class CheckedOutCartRow : IEdictPersistedState
{
    [Id(0)]
    public int ItemCount { get; set; }

    [Id(1)]
    public string Skus { get; set; } = "";
}
