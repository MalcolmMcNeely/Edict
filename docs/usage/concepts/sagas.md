# Sagas

An `EdictSaga<TProgress>` coordinates a multi-step cross-aggregate workflow by reacting to events, holding durable `Progress`, and issuing at most one command per event via `Dispatch`. The floor is zero: a handler that mutates `Progress` and dispatches nothing is a valid handle, not a dropped one. Every handled event commits its dedup-ring slot, and `Progress` rides the same atomic write, so the mutation persists.

```csharp
using Edict.Core.Sagas;

public partial class OrderPaymentSaga : EdictSaga<OrderPaymentProgress>
{
    Task HandleAsync(OrderSubmittedEvent edictEvent)
    {
        Progress.Stage = OrderPaymentStage.PaymentRequested;
        Dispatch(new AuthorizePaymentCommand(edictEvent.OrderId, edictEvent.Amount));
        return Task.CompletedTask;
    }

    Task HandleAsync(PaymentAuthorizedEvent edictEvent)
    {
        Progress.Stage = OrderPaymentStage.Confirmed;
        Dispatch(new ConfirmOrderCommand(edictEvent.OrderId));
        Complete();
        return Task.CompletedTask;
    }
}
```

## Surface

- **`EdictSaga<TProgress>`** (`Edict.Core.Sagas`) — abstract base where `TProgress : IEdictPersistedState, new()`. A consumer declares the saga as a `partial class` (the generator emits the Orleans interface, the implicit stream subscription, and the `DispatchAsync` switch over the consumer's `HandleAsync` overloads) and writes one `Task HandleAsync(TEvent edictEvent)` per subscribed event type.
- **`Progress`** (`protected TProgress`) — durable workflow state. The consumer mutates `Progress` inside `HandleAsync`; it commits atomically with the dedup ring and the staged `SendCommand` effect in one grain-state write.
- **`Dispatch(EdictCommand)`** (`protected void`) — issues at most one command per event. When the event implies a follow-up, call it once; the command is buffered and staged as a `SendCommand` outbox effect after the handler returns, committing atomically with `Progress` and the dedup ring. Not calling it is equally valid: a handler may accumulate into `Progress` and dispatch nothing. A *second* call within the same event handler throws — saga command fan-out is a coordination smell and the API shape makes it structurally unmissable.
- **`Complete()`** (`protected void`) — marks the saga successfully finished. Hard-terminal: buffered like `Dispatch`, applied at the commit, it moves the lifecycle to `Completed` in the same atomic write as `Progress`, unregisters the cap reminder, and causes any later genuinely-new Event to dead-letter. See the lifecycle section below for when not to call it.
- **`OnSagaTimeoutAsync()`** (`protected virtual Task`) — the compensation hook the framework invokes when the absolute cap fires. Override it to compensate; the default dead-letters. See the lifecycle section.
- **`TProgress`** must implement `IEdictPersistedState` and follow the persistence contract (see EDICT011 below).

A saga never `Raise`s — events belong to aggregates. A saga's dedup ring suppresses at-least-once redelivery of any event it has already processed; see [idempotency.md](idempotency.md).

## Accumulate, then act

The "at most one" floor is zero, and that is the whole point of `Progress`. A saga often has to wait for several events before it knows what Command to send, so most handled events mutate `Progress` and dispatch nothing, and only the event that closes the gap dispatches. This is the intended pattern, not a degenerate one: each dispatch-nothing handle still commits, because `Progress` rides the same atomic write as the dedup-ring slot. A crash after a dispatch-nothing handle replays nothing: the accumulated `Progress` is already durable.

```csharp
using Edict.Core.Sagas;

public partial class FulfilmentSaga : EdictSaga<FulfilmentProgress>
{
    Task HandleAsync(ShipmentDispatchedEvent edictEvent)
    {
        // Accumulate. No Command unless this is the shipment that completes the
        // order, so most of these handles mutate Progress and dispatch nothing.
        Progress.DispatchedShipmentIds.Add(edictEvent.ShipmentId);
        return Act(edictEvent.OrderId);
    }

    Task HandleAsync(OrderShipmentPlanEvent edictEvent)
    {
        Progress.PlannedShipmentCount = edictEvent.ShipmentCount;
        return Act(edictEvent.OrderId);
    }

    Task Act(Guid orderId)
    {
        // Act, but only once the accumulated Progress closes the gap.
        if (Progress.PlannedShipmentCount > 0
            && Progress.DispatchedShipmentIds.Count == Progress.PlannedShipmentCount)
        {
            Dispatch(new CloseOrderCommand(orderId));
            Complete();
        }
        return Task.CompletedTask;
    }
}
```

(Both handlers funnel through `Act`, so whichever event arrives last, the final shipment or the plan, is the one that dispatches. The earlier ones are all dispatch-nothing handles that still persist their `Progress`.)

## Lifecycle: the absolute cap

A saga waits for events that may never arrive: a payment callback that never comes, an upstream that stops raising. Every saga therefore carries an **absolute lifetime cap**: a deadline armed once when it handles its first Event, and **never reset** by later activity. The cap is not per-step and not idle-based; it bounds total workflow lifetime so a stalled saga becomes visible and bounded instead of living forever. (Rationale and the rejected per-step and idle alternatives are in ADR-0050.)

**The default is finite.** `EdictSagaOptions.DefaultTimeout` ships at 7 days. Override it per saga with the `[EdictSagaTimeout]` attribute, or opt a saga out of any cap with `Unbounded`:

```csharp
using Edict.Contracts.Sagas;

[EdictSagaTimeout("1.00:00:00")]              // one day; the leading field is DAYS
public partial class OrderPaymentSaga : EdictSaga<OrderPaymentProgress> { /* ... */ }

[EdictSagaTimeout(Unbounded = true)]          // no cap; beats even a finite silo-wide default
public partial class LongRunningCoordinator : EdictSaga<CoordinatorProgress> { /* ... */ }
```

The duration is a `TimeSpan`-parseable literal whose **leading field is days**, so `"24:00:00"` is **24 days**, not 24 hours; write one day as `"1.00:00:00"`.

**`Complete()` is the terminal success path.** Call it from a handler when the workflow is genuinely done. The lifecycle moves to `Completed` atomically with the handler's `Progress` and any dispatched Command, and the cap reminder is unregistered. After that, a **genuinely-new Event the saga handles** (a new `EventId`, not an at-least-once redelivery; those still dedup) dead-letters with `EdictSagaTerminalException` rather than silently re-handling and re-dispatching its Commands. A saga subscribes to a whole domain stream, so it also receives event types it has no `HandleAsync` for; those are ignored at a terminal saga exactly as while it is live, so completing a saga that shares a stream with other event types does not dead-letter that unrelated traffic.

**When not to call `Complete()`.** A saga whose key may legitimately receive a later Event (a long-lived coordinator that keeps reacting) should never call it. Leaving it live keeps it re-openable; the absolute cap is its only terminal path. `Complete()` is opt-in precisely so a re-openable saga is the default and a finished workflow is the deliberate, marked case.

**When the cap fires**, the framework gives the saga one chance to compensate through `OnSagaTimeoutAsync()`:

```csharp
protected override Task OnSagaTimeoutAsync()
{
    Progress.Stage = OrderPaymentStage.TimedOut;
    Dispatch(new CancelOrderCommand(this.GetPrimaryKey(), "saga_timed_out"));
    return Task.CompletedTask;
}
```

The override may mutate `Progress` and `Dispatch` at most one compensating Command; both commit atomically with the `TimedOut` terminal write. **The default (no override) dead-letters** the fired cap with `EdictSagaTimeoutException`, so a finite-capped saga that times out without a compensation path surfaces loudly on the dead-letter projection rather than silently stranding the workflow.

## Analyzer rules

- **EDICT011** — `TProgress` (and every persisted state type) must carry `[GenerateSerializer]`, `[Alias("literal")]`, and `[Id(n)]` on every declared public property. The `[Alias]` argument must be a string literal — `nameof(T)` is rejected because it defeats the rename-survival guarantee.
- **EDICT017** — call `Dispatch` with a concrete-typed command, not an `EdictCommand`-typed variable; the interceptor fast path needs the static type to intercept the call site.
- **EDICT020** — the `[EdictSagaTimeout]` duration literal must parse to a positive `TimeSpan`. A non-parseable, zero, or negative literal is an error.
- **EDICT021** — `[EdictSagaTimeout]` cannot set both a `Duration` and `Unbounded = true`; they are mutually exclusive.
- **EDICT022** — an `OnSagaTimeoutAsync` override on a saga declared `Unbounded = true` is dead code (the cap never fires) and is flagged as a warning.

The `partial` modifier is required by the generator; if it is missing, the generator emits no dispatch switch and the saga fails at runtime rather than at compile time. (No partial-analyzer covers sagas today.)

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Saga`, `Saga Timeout`, `Complete`, `Compensation`, `EdictCommand`, `Event`.
- Concepts — [commands.md](commands.md), [events.md](events.md), [event-handlers.md](event-handlers.md), [schedules.md](schedules.md), [idempotency.md](idempotency.md), [dead-letter.md](dead-letter.md), [telemetry.md](telemetry.md).
- ADRs — [0016 — Saga model](../../adr/0016-saga-model.md), [0050 — Saga absolute lifetime cap](../../adr/0050-saga-absolute-lifetime-cap.md).
