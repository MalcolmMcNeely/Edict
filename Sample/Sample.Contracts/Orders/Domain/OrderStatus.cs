namespace Sample.Contracts.Orders.Domain;

/// <summary>Lifecycle of an order aggregate.</summary>
public enum OrderStatus
{
    Open,
    Submitted,
    Confirmed,
    Cancelled,
    Shipped,
}
