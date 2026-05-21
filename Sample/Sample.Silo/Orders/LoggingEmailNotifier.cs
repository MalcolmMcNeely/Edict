using Microsoft.Extensions.Logging;

namespace Sample.Silo.Orders;

/// <summary>
/// Default <see cref="IEmailNotifier"/> for the Sample app: logs a single
/// structured line carrying the order id and the framework-stamped
/// <c>EventId</c> as the canonical external-API idempotency key. No real SMTP
/// — the Sample stays self-contained per <c>CONTEXT.md</c>.
/// </summary>
public sealed class LoggingEmailNotifier(ILogger<LoggingEmailNotifier> logger) : IEmailNotifier
{
    public Task SendOrderPlacedAsync(Guid orderId, Guid eventId)
    {
        logger.LogInformation(
            "Simulated email send for order {OrderId} (idempotency key {EventId}).",
            orderId,
            eventId);
        return Task.CompletedTask;
    }
}
