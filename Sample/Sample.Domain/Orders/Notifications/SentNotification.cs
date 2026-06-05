namespace Sample.Domain.Orders.Notifications;

/// <summary>
/// One record of an outbound notification the Event Handler sent for an order.
/// Doubles as the JSON body the silo's <see cref="HttpEmailNotifier"/> POSTs to
/// the Web-hosted sink and as the row the <see cref="INotificationsStore"/>
/// hands back to the Orders view. <c>EventId</c> is the framework-stamped
/// idempotency key the external API would dedup on.
/// </summary>
public sealed record SentNotification(Guid OrderId, Guid EventId);
