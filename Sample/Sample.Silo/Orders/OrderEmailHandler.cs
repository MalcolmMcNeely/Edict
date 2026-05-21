using Edict.Core.EventHandler;

using Microsoft.Extensions.Logging;

using Sample.Contracts.Orders.Events;

namespace Sample.Silo.Orders;

/// <summary>
/// Sample <see cref="EdictEventHandler"/>: reacts to
/// <see cref="OrderPlacedEvent"/> by simulating an order-confirmation email
/// send. The Sample app stays self-contained per CONTEXT.md — no real SMTP — but
/// rides the same Azurite-backed Outbox/dead-letter pipeline that ships in
/// production, so a transient log of the simulated send still appears off the
/// stream-callback hot path with framework-managed retry/backoff. The canonical
/// external-API idempotency-key pattern (<c>evt.EventId</c>) is shown in the log.
/// </summary>
public sealed partial class OrderEmailHandler(ILogger<OrderEmailHandler> logger) : EdictEventHandler
{
    public Task Handle(OrderPlacedEvent evt)
    {
        logger.LogInformation(
            "Simulated email send for order {OrderId} (idempotency key {EventId}).",
            evt.OrderId,
            evt.EventId);
        return Task.CompletedTask;
    }
}
