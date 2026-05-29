using Edict.Core.EventHandler;

using Sample.Contracts.Orders.Events;

namespace Sample.Domain.Diagnostics.EventHandlers;

// Diagnostics-only: throws when the cancel reason starts with "POISON:" so the
// InvokeHandler outbox effect retries until OutboxMaxAttempts exhausts, then
// the engine promotes the entry to Dead Letter. Production-shaped code does
// not live in Diagnostics/.
public sealed partial class PoisonAuditEventHandler : EdictEventHandler
{
    public Task Handle(OrderCancelledEvent edictEvent)
    {
        if (edictEvent.Reason.StartsWith("POISON:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"PoisonAuditEventHandler refused to audit cancel reason '{edictEvent.Reason}'.");
        }

        return Task.CompletedTask;
    }
}
