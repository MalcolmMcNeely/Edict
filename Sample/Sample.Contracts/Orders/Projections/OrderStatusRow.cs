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

    [Id(2)]
    public DateTimeOffset? PlacedAt { get; set; }

    [Id(3)]
    public DateTimeOffset? SubmittedAt { get; set; }

    [Id(4)]
    public DateTimeOffset? AuthorizedAt { get; set; }

    [Id(5)]
    public DateTimeOffset? FulfilledAt { get; set; }

    [Id(6)]
    public DateTimeOffset? ShippedAt { get; set; }
}
