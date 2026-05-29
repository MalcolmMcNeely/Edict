# Event handlers

An `EdictEventHandler` subscribes to a stream and performs external side effects — sending email, calling an HTTP API, writing to a non-Edict store. It is a terminal role: no `Raise`, no `Dispatch`.

```csharp
using Edict.Core.EventHandler;

public sealed partial class OrderEmailEventHandler(IEmailNotifier notifier) : EdictEventHandler
{
    public Task Handle(OrderPlacedEvent edictEvent) =>
        notifier.SendOrderPlacedAsync(edictEvent.OrderId, edictEvent.EventId);
}
```

## Surface

- **`EdictEventHandler`** (`Edict.Core.EventHandler`) — abstract base. A consumer declares the handler as a `partial class` (the generator emits the Orleans interface, the implicit stream subscription, the dispatch switch, and the `HandlesType` pre-flight) and writes one `Task Handle(TEvent edictEvent)` per subscribed event type.
- The handler runs **deferred**: the stream-callback path stages an `InvokeHandler` outbox entry, and the actual `Handle` call runs inside the outbox drain. The handler inherits per-entry retry/backoff and dead-letter promotion on `MaxAttempts` exhaustion from the outbox.
- **External-API idempotency is the consumer's responsibility.** The canonical pattern is to pass `EdictEvent.EventId` as the downstream API's idempotency key; the example above passes `edictEvent.EventId` to the email notifier for exactly this reason.
- Failure classification is uniform: every throw from `Handle` is treated as transient, retried with exponential backoff, and dead-lettered at `MaxAttempts`. There is no transient/permanent discriminator in the consumer's hand. See [dead-letter.md](dead-letter.md).

## Analyzer rules

There is no analyzer-enforced `partial` modifier or `Handle` signature analyzer on `EdictEventHandler` today; the generator emits the dispatch switch only when the class is `partial`, and a missing modifier fails at runtime rather than at compile time. The events the handler subscribes to are gated by [events.md](events.md)'s rules (EDICT003, EDICT007, EDICT008).

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Event Handler`, `Event`, `Idempotency Base`.
- Concepts — [events.md](events.md), [idempotency.md](idempotency.md), [dead-letter.md](dead-letter.md), [sagas.md](sagas.md), [telemetry.md](telemetry.md).
