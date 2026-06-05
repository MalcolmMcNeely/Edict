---
name: edict-authoring
description: Use this skill when working on a consumer app built on Edict and adding a new feature — a new Command, Event, Command Handler, Event Handler, Saga, Projection Builder, or List Projection Builder. Walks the role decision tree before any code is written.
---

# Authoring a feature in an Edict consumer app

Use this skill the moment you decide to add behaviour to an Edict consumer app. Pick the right grain role first; cross-checking the existing handler inventory is the second move, not the last.

## The role decision tree

A new feature is *always* one of five grain roles plus the validator DI-service role. Pick deliberately.

- **Command Handler** — the Guid-keyed aggregate. Use when the new behaviour is a state transition the user (or another system) is *asking for*. Lives on `EdictCommandHandler<TState>`; named `{Name}CommandHandler`. Handles `EdictCommand` subclasses, mutates durable `State`, returns an `EdictCommandResult`, and may raise `EdictEvent`s.
- **Command Validator** — the precondition gate. A stateless DI service (not a grain) resolved by the handler's activation each turn. Lives on `EdictCommandValidator<TCommand>`; named `{Name}CommandValidator`. Reads current state, never mutates, and yields a `Rejected` result on failure. The line between Validator and `HandleAsync` is mutation: knowable-from-current-state → Validator; only-knowable-while-mutating → `HandleAsync`.
- **Event Handler** — the terminal side-effect grain. Use when something has *happened* and the consequence is external (email, HTTP call, non-Edict store). Lives on `EdictEventHandler`; named `{Name}EventHandler`. Never owns events, never calls `Raise` or `Dispatch`.
- **Saga** — the coordinator. Use when an event needs to fan into at most one follow-up Command, possibly on a different aggregate. Lives on `EdictSaga<TProgress>`; at most one Command per handled Event via `Dispatch`. Do not reconstruct progress by replay; the durable `Progress` is the source of truth.
- **Projection Builder** — the in-grain read model. Use when a small, single-grain forward-only view is enough. Edict is event-driven, not event-sourced — projections only ever see events from subscription forward.
- **List Projection Builder** — the external read model. Use when the read model grows beyond what fits comfortably in grain state. The durable row lives in an external store; the grain holds a transient last-touched-slot cache.

## Always check the inventory before authoring

Before you write the new class, call **`edict_list_handlers`** to see every existing handler and validator in the solution, and **`edict_list_route_keys`** to see which Guid keys are taken. `edict_list_handlers` reports Command Validators alongside the five grain roles, so missing or duplicate validator coverage shows up in the same tool call. Two reasons:

- A handler or validator for the Command or Event already exists. The right move is to extend it or wire to it, not to write a parallel one.
- The `[EdictRouteKey]` Guid you were about to mint already routes a different Command — that is a runtime collision and a silent bug.

These two MCP tools are the load-bearing trigger for this skill: invoke `edict_list_handlers` and `edict_list_route_keys` when adding a feature, before suggesting code.

## Authoring a Command Handler

Derive from `EdictCommandHandler<TState>` in `Edict.Core.Commands` and write one `Task<EdictCommandResult> HandleAsync(TCommand command)` per handled Command. Mutate the durable `State` directly, return `Accepted` or `Rejected`, and `Raise` an Event when something the rest of the system cares about happened.

**`State` persists whenever `HandleAsync` completes** — on both `Accepted` and `Rejected`, and independent of whether you raised an Event. You do not opt in to persistence and there is no `Save()`/`MarkChanged()` call: a completing handler's `State` is committed, full stop. This makes the accumulate-now-act-later pattern a first-class move — an `AddItemToCart` that mutates `State` and raises nothing builds up the basket durably, and a later `CheckoutCart` reads that accumulated state and raises the one Event that carries it. The `EdictCommandResult` is the caller's answer only; it gates nothing, and raised Events publish on the strength of `Raise` having been called, not on the result.

**Never mutate `State` on a path that then throws.** A throw is the framework's only discard signal: it rolls the partial `State` mutation back to the last durable snapshot, drops any buffered Events, and dead-letters. So a handler that mutates a few fields and then throws loses *all* of that turn's mutation, not just the part after the throw. If a condition should reject, return `Rejected` — do not throw to "cancel" a mutation, and do not leave `State` half-written on a path that can throw downstream. Validate-then-mutate, never mutate-then-maybe-throw.

```csharp
Task<EdictCommandResult> HandleAsync(CheckoutCartCommand command)
{
    if (State.Skus.Count == 0)
    {
        // Reject — do NOT throw — to decline without committing anything this turn.
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
            [new EdictRejectionReason("cart_empty", "Cart has no items.")]));
    }

    Raise(new CartCheckedOutEvent(command.CartId, State.Skus.Count, State.Skus.ToArray()));
    return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
}
```

## Authoring a Command Validator

Derive from `EdictCommandValidator<TCommand>` in `Edict.Core.Commands`. The base is a thin shim over FluentValidation's `AbstractValidator<TCommand>` — the rule DSL is unchanged. Author rules with `RuleFor(c => c.Property)`, gate them with `When(...)`, and attach a stable identifier with `WithErrorCode("snake_case_code")`. The framework copies each `ValidationFailure.ErrorCode` onto the resulting `EdictRejectionReason.Code` and ships the failure list back as `EdictCommandResult.Rejected` — failures are never thrown.

```csharp
public sealed class PlaceOrderCommandValidator : EdictCommandValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(c => c.CustomerReference)
            .NotEmpty()
            .WithErrorCode("customer_reference_required");
    }
}
```

Validators run in the same Orleans activation turn as `HandleAsync`. Read the current aggregate state from `ValidationContext.RootContextData[SemanticConventions.Validation.GrainStateKey]` — override `GetValidationState()` on the Command Handler to expose it, then pull it inside a rule:

```csharp
RuleFor(c => c).Custom((command, ctx) =>
{
    if (ctx.RootContextData[SemanticConventions.Validation.GrainStateKey] is OrderState { Status: OrderStatus.Shipped })
    {
        ctx.AddFailure(new ValidationFailure(nameof(command), "Order already shipped.") { ErrorCode = "order_already_shipped" });
    }
});
```

Decide between Validator and `HandleAsync` by the mutation line: if the rejection is knowable from current state alone, it belongs in the Validator; if it is only knowable mid-mutation (a derived value computed during the handler), it stays in `HandleAsync` and returns its own `Rejected`. Validators are stateless and have no `Raise`, no `Dispatch`, and no access to streams or the outbox.

## Authoring a Saga

Derive from `EdictSaga<TProgress>` in `Edict.Core.Sagas` and write one `Task HandleAsync(TEvent edictEvent)` per subscribed Event. Each handler mutates the durable `Progress` and issues at most one Command via `Dispatch`. "At most one" means the floor is zero: a handler that mutates `Progress` and dispatches nothing is a valid handle, not a dropped one. The mutation commits on the same atomic write as the dedup-ring slot, so the accumulate-now, act-on-a-later-Event pattern is first-class. Beyond that, a saga has a lifecycle every author has to decide about.

**Every saga has an absolute lifetime cap.** It is armed once, on the saga's first handled Event, and never reset by later activity: it bounds total workflow lifetime, not idle time and not per step. The default is finite: a saga inherits the silo-wide `EdictSagaOptions.DefaultTimeout`, which ships at 7 days. Override it per saga, or opt out entirely:

```csharp
[EdictSagaTimeout("1.00:00:00")]              // one day; the leading field is DAYS, so "24:00:00" is 24 days
public partial class OrderPaymentSaga : EdictSaga<OrderPaymentProgress> { /* ... */ }

[EdictSagaTimeout(Unbounded = true)]          // no cap; use only for a genuinely open-ended coordinator
public partial class LongRunningCoordinator : EdictSaga<CoordinatorProgress> { /* ... */ }
```

**Call `Complete()` when the workflow is genuinely finished**, from the terminal handler alongside the last `Dispatch`. It is hard-terminal: the lifecycle moves to `Completed` in the same atomic write, the cap reminder is dropped, and any later genuinely-new Event the saga *handles* dead-letters instead of restarting the workflow. Unrelated event types the saga receives off its shared domain stream stay ignored, so completing a saga that shares a stream is safe.

**Decide deliberately whether to call `Complete()` at all.** A saga whose key may legitimately receive a later Event (a long-lived coordinator that keeps reacting) should *not* call it; leave it live and rely on the cap as its only terminal path. Calling `Complete()` on such a saga turns a normal later Event into a dead-letter. The rule: call `Complete()` only when no further Event for this key can be part of the workflow.

**Override `OnSagaTimeoutAsync()` to compensate when the cap fires.** The override may mutate `Progress` and `Dispatch` at most one compensating Command, both of which commit atomically with the timeout. The default (no override) dead-letters the fired cap, so a finite-capped saga that stalls surfaces loudly rather than vanishing.

```csharp
protected override Task OnSagaTimeoutAsync()
{
    Dispatch(new CancelOrderCommand(this.GetPrimaryKey(), "saga_timed_out"));
    return Task.CompletedTask;
}
```

`edict_list_handlers` reports each saga's effective cap (a duration, `unbounded`, or `default`) alongside its role, so the inventory check above also tells you which sagas already carry an explicit `[EdictSagaTimeout]`.

## Authoring a schedule

A schedule is recurring or timeout work driven from inside a Command Handler or a Saga. It is not a sixth grain role: you add it to an existing Command Handler or Saga, never to an Event Handler or a Projection Builder. Reach for it when a handler needs to do something *again later* on a fixed cadence (poll a gateway, fulfil the next line, renew a lease) instead of blocking the current turn or wiring a raw Orleans timer.

A schedule fires a **message**, never a delegate. The message is a record deriving from `EdictScheduleMessage` in `Edict.Contracts.Schedules`, carrying an `[Alias]` exactly like a Command or Event so it round-trips on the wire. The framework persists the message bytes in the grain envelope, and on each fire deserializes it and routes it back through the same generator-emitted dispatch the handler uses for Commands:

```csharp
[Alias("fulfill-next-line")]
public sealed record FulfillNextLine : EdictScheduleMessage;
```

Start the schedule from a normal `HandleAsync` by calling the protected `Schedule(message, every, timeout)`. The cadence is declared once, here, as a single fixed `every:` interval (no jitter, no per-fire reschedule). The first fire is at `+every`:

```csharp
Task<EdictCommandResult> HandleAsync(StartFulfillmentCommand command)
{
    State.OrderId = command.OrderId;
    Schedule(new FulfillNextLine(), every: TimeSpan.FromSeconds(2));
    return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
}
```

Handle each fire with a `Task<EdictScheduleResult> HandleAsync(TMessage)` overload alongside your Command handlers. It re-enters the full handler lifecycle (mutate `State`, `Raise` events, `Dispatch` from a saga) and answers exactly one of two outcomes: keep firing on the cadence, or stop. On a **Command Handler** use the protected `Continue()` / `Complete()` helpers, which return the result already wrapped in a `Task`:

```csharp
async Task<EdictScheduleResult> HandleAsync(FulfillNextLine message)
{
    var pendingIndex = State.Lines.FindIndex(line => line.Status == LineItemFulfillmentStatus.Pending);
    if (pendingIndex < 0)
    {
        return await Complete();
    }

    State.Lines[pendingIndex] = State.Lines[pendingIndex] with { Status = LineItemFulfillmentStatus.Fulfilled };
    Raise(new LineItemFulfilledEvent(State.OrderId, State.Lines[pendingIndex].LineItemId));
    return await Continue();
}
```

A **Saga** has no `Continue()` / `Complete()` helpers (its own `Complete()` is the saga lifecycle terminal). Construct the result directly:

```csharp
Task<EdictScheduleResult> HandleAsync(PollGatewayMessage message)
{
    Progress.Polls++;
    if (Progress.Polls >= 2)
    {
        Dispatch(new ConfirmSettlementCommand(Progress.PaymentId));
        return Task.FromResult<EdictScheduleResult>(new EdictScheduleResult.Complete());
    }
    return Task.FromResult<EdictScheduleResult>(new EdictScheduleResult.Continue());
}
```

A one-shot delayed action is just `every: X` plus `Complete()` on the first fire.

**Every schedule has a timeout cap, armed once at registration and never reset by a fire** (a dead-man's switch on total schedule lifetime). Omitting `timeout:` inherits the silo default; pass an explicit `TimeSpan` to cap shorter, or `EdictSchedule.Unbounded` to opt a legitimately perpetual schedule out of any cap. A Command Handler schedule inherits `EdictCommandHandlerScheduleOptions.DefaultTimeout` (see the `edict-silo-wiring` skill); a Saga schedule is instead bounded by the saga's own `[EdictSagaTimeout]` cap.

To compensate when the cap fires, write an `OnScheduleTimeoutAsync(TMessage)` overload taking the same message type. It runs the compensation atomically with the schedule's removal. Without it, a fired cap dead-letters (loudly, never silently):

```csharp
Task OnScheduleTimeoutAsync(PollGatewayMessage message)
{
    Dispatch(new AbandonSettlementCommand(Progress.PaymentId));
    return Task.CompletedTask;
}
```

`edict_list_handlers` reports, per Command Handler and Saga, whether it registers a schedule and the source of that schedule's timeout cap (`inheritsSiloDefault` for a Command Handler, `inheritsSagaCap` for a Saga). It does not report the per-schedule `timeout:` literal, so the inventory tells you *which* handlers schedule, not the exact cap value at each call site.

## Reading a List Projection, and read-your-writes

A List Projection Builder's read model is read through `IEdictProjectionReader<TRow>` (DI-injected, the read-side mirror of `IEdictSender`), never by touching the store. Each read returns an `EdictProjectionRead<TRow>` — the row plus an `EdictReadStatus` — so a plain read takes `.Row`:

```csharp
OrderStatusRow? row = (await ordersReader.GetAsync(orderId.ToString(), "status")).Row;
```

To read your own write back without a poll-and-retry loop, feed the `EdictCursor` from the Command's `Accepted` result as `after:`. The read waits, briefly and boundedly, until the work that Command set in motion is visible, then answers:

```csharp
var result = await sender.SendAsync(new PlaceOrderCommand(orderId, "REF-001"));
if (result is EdictCommandResult.Accepted accepted)
{
    EdictProjectionRead<OrderStatusRow> read =
        await ordersReader.GetAsync(orderId.ToString(), "status", after: accepted.Cursor);

    // read.Status is CursorReached and read.Row reflects the placement, on this call.
}
```

The cursor names a framework-stamped correlation that propagates across the whole chain — a Command's raised Events and a Saga's dispatched Commands inherit it — so the cursor a Command returns reaches a projection effect that lands *downstream of a Saga*, not just the immediate write. You never author the correlation: keep returning `new EdictCommandResult.Accepted()` and the runtime stamps the cursor after the handler returns.

`CursorReached` is **any-applied**: it means at least the correlation's first effect on this projection is visible, not that every effect has landed. Where exact read-your-writes matters, prefer one Event per Command, so the correlation has a single effect on the projection and "first applied" equals "fully applied". On a bounded-wait timeout the read returns `CursorTimedOut` with the latest available row — that is lag, not a fault, so never wrap a read in a `try`/`catch` expecting a stale-read exception. Pass `Timeout.InfiniteTimeSpan` to wait indefinitely; an omitted timeout is always bounded (it falls back to `EdictOptions.ProjectionReadTimeout`, never to infinite).

## When to look up a term

When the consumer asks "what is a Saga?" / "what is a Projection Builder?" / "what does Command Validator mean here?", or when picking between two role names whose distinction is fuzzy, invoke **`edict_describe_glossary_term`** for the authoritative one-line definition and its `_Avoid_` list. The optional `Edict` prefix on the query is elidable — `Saga`, `saga`, and `EdictSaga` all resolve. Use this before guessing a definition from the role name.

## Naming and brand prefix

Consumer subclasses are `{Name}{Role}` — never `Grain`-suffixed. Examples: `OrderCommandHandler`, `OrderPaymentSaga`, `OrdersByStatusProjectionBuilder`. The `Edict`-prefix is reserved for the framework surface itself; do not add it to your subclasses.

Both projection species share the one `{Name}ProjectionBuilder` suffix, storage-neutral by design: the base type disambiguates them — `EdictProjectionBuilder` for an in-grain view, `EdictListProjectionBuilder<TRow>` for an external-store read model — so the class name never carries a storage word like `Table`.

## See also

- For the contract attributes (`[EdictRouteKey]`, `[EdictStream]`, `[EdictTelemeterized]`, MessagePack rules): see the `edict-contracts` skill.
- For wiring the new grain into `Program.cs`: see the `edict-silo-wiring` skill.
- For testing the new grain: see the `edict-testing` skill.
- For diagnosing failures in the new grain: see the `edict-diagnostics` skill.
