namespace Sample.Silo.Orders;

/// <summary>Lifecycle of an order aggregate.</summary>
public enum OrderStatus
{
    Open,
    Submitted,
    Confirmed,
    Cancelled,
}
