namespace Sample.Api.Orders;

public sealed record AddLineItemRequest(string Sku, int Quantity);
