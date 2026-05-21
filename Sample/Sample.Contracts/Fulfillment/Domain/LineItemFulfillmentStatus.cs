namespace Sample.Contracts.Fulfillment.Domain;

/// <summary>Fulfillment lifecycle of a single line on an order.</summary>
public enum LineItemFulfillmentStatus
{
    Pending,
    Fulfilled,
}
