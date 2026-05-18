using Azure;
using Azure.Data.Tables;

namespace Sample.Silo.Orders;

public sealed class OrderStatusRow : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Status { get; set; } = "Open";
    public int ItemCount { get; set; }
}
