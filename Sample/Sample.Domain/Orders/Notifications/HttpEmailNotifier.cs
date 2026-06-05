using System.Net.Http.Json;

using Microsoft.Extensions.Logging;

namespace Sample.Domain.Orders.Notifications;

/// <summary>
/// <see cref="IEmailNotifier"/> that reaches out of the silo to the Web-hosted
/// notifications sink: it POSTs a <see cref="SentNotification"/> per send,
/// modelling the Event Handler calling an external HTTP API rather than the Web
/// reading grain state. A sink-unreachable condition (the Web still starting, a
/// transient outage) is logged and swallowed, not thrown: a demo notification
/// must never push its <c>InvokeHandler</c> outbox effect into the
/// retry-then-dead-letter path.
/// </summary>
public sealed class HttpEmailNotifier(HttpClient httpClient, ILogger<HttpEmailNotifier> logger) : IEmailNotifier
{
    public async Task SendOrderPlacedAsync(Guid orderId, Guid eventId)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync("/notifications", new SentNotification(orderId, eventId));
            response.EnsureSuccessStatusCode();
        }
        catch (Exception sinkUnreachable)
        {
            // Best-effort: any send failure (Web still starting, transient outage,
            // a circuit-breaker open from the resilience handler) is logged and
            // dropped. Letting it escape would fault the InvokeHandler outbox effect
            // and dead-letter a demo notification, which the demo must never do.
            logger.LogWarning(
                sinkUnreachable,
                "Notifications sink unreachable for order {OrderId} (idempotency key {EventId}); dropping this demo notification.",
                orderId,
                eventId);
        }
    }
}
