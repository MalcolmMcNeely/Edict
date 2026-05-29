using Edict.Core.EventHandler;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sample.Contracts.Orders.Events;

namespace Sample.Domain.Orders.EventHandlers;

/// <summary>
/// Sample <see cref="EdictEventHandler"/>: reacts to
/// <see cref="OrderPlacedEvent"/> by delegating to the consumer-injected
/// <see cref="IEmailNotifier"/>. When no notifier is registered (e.g. an
/// <c>EdictTestApp</c> that does not call <c>.Replace&lt;IEmailNotifier&gt;</c>)
/// it falls back to a structured log line so the deferred-invocation path
/// still surfaces on the timeline without forcing every test to fake a
/// collaborator.
/// </summary>
public sealed partial class OrderEmailEventHandler(ILogger<OrderEmailEventHandler> logger) : EdictEventHandler
{
    public Task Handle(OrderPlacedEvent edictEvent)
    {
        if (ServiceProvider.GetService<IEmailNotifier>() is { } notifier)
        {
            return notifier.SendOrderPlacedAsync(edictEvent.OrderId, edictEvent.EventId);
        }

        logger.LogInformation(
            "Simulated email send for order {OrderId} (idempotency key {EventId}).",
            edictEvent.OrderId,
            edictEvent.EventId);
        return Task.CompletedTask;
    }
}
