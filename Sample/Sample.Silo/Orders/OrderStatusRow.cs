namespace Sample.Silo.Orders;

public sealed class OrderStatusRow
{
    public string Status { get; set; } = "Open";
    public int ItemCount { get; set; }
}
