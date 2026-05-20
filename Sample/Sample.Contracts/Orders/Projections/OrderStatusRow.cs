namespace Sample.Contracts.Orders.Projections;

public sealed class OrderStatusRow
{
    public string Status { get; set; } = "Open";
    public int ItemCount { get; set; }
}
