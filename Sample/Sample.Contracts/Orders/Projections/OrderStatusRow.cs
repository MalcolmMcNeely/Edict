using Edict.Contracts.Persistence;

namespace Sample.Contracts.Orders.Projections;

[GenerateSerializer]
[Alias("Sample.Contracts.Orders.Projections.OrderStatusRow")]
public sealed class OrderStatusRow : IEdictPersistedState
{
    [Id(0)]
    public string Status { get; set; } = "Open";

    [Id(1)]
    public int ItemCount { get; set; }
}
