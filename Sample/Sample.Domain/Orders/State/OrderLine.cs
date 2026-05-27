namespace Sample.Domain.Orders.State;

/// <summary>A single line on an order. Part of the persisted aggregate state.</summary>
[GenerateSerializer]
[Alias("Sample.Silo.Orders.OrderLine")]
public sealed record OrderLine
{
    [Id(0)]
    public string Sku { get; init; } = "";

    [Id(1)]
    public int Quantity { get; init; }

    [Id(2)]
    public Guid LineItemId { get; init; }
}
