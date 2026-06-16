using Sample.Contracts.Fulfillment.Commands;
using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Orders.Events;
using Sample.Contracts.Payments.Commands;
using Sample.Contracts.Payments.Events;

namespace Sample.Web.Components.Pages.Audit;

public static class AuditMessageView
{
    public static IReadOnlyList<AuditMessageField> Describe(object message) =>
        message switch
        {
            PlaceOrderCommand place =>
            [
                new("Order", place.OrderId.ToString()),
                new("Customer reference", place.CustomerReference),
            ],
            AddLineItemCommand addLineItem =>
            [
                new("Order", addLineItem.OrderId.ToString()),
                new("Line item", addLineItem.LineItemId.ToString()),
                new("SKU", addLineItem.Sku),
                new("Quantity", addLineItem.Quantity.ToString()),
            ],
            SubmitOrderCommand submit =>
            [
                new("Order", submit.OrderId.ToString()),
                new("Amount", submit.Amount.ToString("0.00")),
            ],
            OrderPlacedEvent placed =>
            [
                new("Order", placed.OrderId.ToString()),
            ],
            LineItemAddedEvent lineItemAdded =>
            [
                new("Order", lineItemAdded.OrderId.ToString()),
                new("Line item", lineItemAdded.LineItemId.ToString()),
                new("SKU", lineItemAdded.Sku),
                new("Quantity", lineItemAdded.Quantity.ToString()),
            ],
            OrderSubmittedEvent submitted =>
            [
                new("Order", submitted.OrderId.ToString()),
                new("Amount", submitted.Amount.ToString("0.00")),
            ],
            ConfirmOrderCommand confirm =>
            [
                new("Order", confirm.OrderId.ToString()),
            ],
            OrderConfirmedEvent confirmed =>
            [
                new("Order", confirmed.OrderId.ToString()),
                new("Line items", confirmed.LineItemIds.Count.ToString()),
            ],
            AuthorizePaymentCommand authorize =>
            [
                new("Order", authorize.OrderId.ToString()),
                new("Amount", authorize.Amount.ToString("0.00")),
            ],
            PaymentAuthorizedEvent authorized =>
            [
                new("Order", authorized.OrderId.ToString()),
            ],
            PaymentDeclinedEvent declined =>
            [
                new("Order", declined.OrderId.ToString()),
            ],
            StartFulfillmentCommand startFulfillment =>
            [
                new("Order", startFulfillment.OrderId.ToString()),
                new("Line items", startFulfillment.LineItemIds.Count.ToString()),
            ],
            _ => [new("Message", message.GetType().Name)],
        };
}
