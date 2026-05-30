# Sagas

An `EdictSaga<TProgress>` coordinates a multi-step cross-aggregate workflow by reacting to events and issuing exactly one command per event via `Dispatch`.

```csharp
using Edict.Core.Sagas;

public partial class OrderPaymentSaga : EdictSaga<OrderPaymentProgress>
{
    public Task HandleAsync(OrderSubmittedEvent edictEvent)
    {
        Progress.Stage = OrderPaymentStage.PaymentRequested;
        Dispatch(new AuthorizePaymentCommand(edictEvent.OrderId, edictEvent.Amount));
        return Task.CompletedTask;
    }

    public Task HandleAsync(PaymentAuthorizedEvent edictEvent)
    {
        Progress.Stage = OrderPaymentStage.Confirmed;
        Dispatch(new ConfirmOrderCommand(edictEvent.OrderId));
        return Task.CompletedTask;
    }
}
```

## Surface

- **`EdictSaga<TProgress>`** (`Edict.Core.Sagas`) — abstract base where `TProgress : IEdictPersistedState, new()`. A consumer declares the saga as a `partial class` (the generator emits the Orleans interface, the implicit stream subscription, and the `DispatchAsync` switch over the consumer's `HandleAsync` overloads) and writes one `Task HandleAsync(TEvent edictEvent)` per subscribed event type.
- **`Progress`** (`protected TProgress`) — durable workflow state. The consumer mutates `Progress` inside `HandleAsync`; it commits atomically with the dedup ring and the staged `SendCommand` effect in one grain-state write.
- **`Dispatch(EdictCommand)`** (`protected void`) — issues the single command this event implies. Buffered and staged as a `SendCommand` outbox effect after the handler returns; commits atomically with `Progress` and the dedup ring. A second call within the same event handler throws — saga command fan-out is a coordination smell and the API shape makes it structurally unmissable.
- **`TProgress`** must implement `IEdictPersistedState` and follow the persistence contract (see EDICT011 below).

A saga never `Raise`s — events belong to aggregates. A saga's dedup ring suppresses at-least-once redelivery of any event it has already processed; see [idempotency.md](idempotency.md).

## Analyzer rules

- **EDICT011** — `TProgress` (and every persisted state type) must carry `[GenerateSerializer]`, `[Alias("literal")]`, and `[Id(n)]` on every declared public property. The `[Alias]` argument must be a string literal — `nameof(T)` is rejected because it defeats the rename-survival guarantee.
- **EDICT017** — call `Dispatch` with a concrete-typed command, not an `EdictCommand`-typed variable; the interceptor fast path needs the static type to intercept the call site.

The `partial` modifier is required by the generator; if it is missing, the generator emits no dispatch switch and the saga fails at runtime rather than at compile time. (No partial-analyzer covers sagas today.)

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Saga`, `EdictCommand`, `Event`.
- Concepts — [commands.md](commands.md), [events.md](events.md), [event-handlers.md](event-handlers.md), [idempotency.md](idempotency.md), [dead-letter.md](dead-letter.md), [telemetry.md](telemetry.md).
