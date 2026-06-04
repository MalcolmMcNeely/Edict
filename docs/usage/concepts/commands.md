# Commands

An `EdictCommand` expresses intent to change state, routed to one aggregate grain by the single `Guid` property carrying `[EdictRouteKey]`.

```csharp
using Edict.Contracts.Commands;

public sealed partial record PlaceOrderCommand(
    [property: EdictRouteKey] Guid OrderId,
    string CustomerReference) : EdictCommand;
```

Dispatched through the DI-injected `IEdictSender`:

```csharp
EdictCommandResult result = await sender.SendAsync(new PlaceOrderCommand(orderId, "acme/42"));
```

## Surface

- **`EdictCommand`** (`Edict.Contracts.Commands`, abstract record) — carries `CommandId`, framework-assigned. Concrete commands derive as `partial record`.
- **`[EdictRouteKey]`** (`Edict.Contracts.Commands`) — marks the one `Guid` property that selects the aggregate grain. Exactly one per command.
- **`EdictCommandResult`** (`Edict.Contracts.Commands`) — closed hierarchy:
  - `EdictCommandResult.Accepted` — carries no domain data.
  - `EdictCommandResult.Rejected(IReadOnlyList<EdictRejectionReason> Reasons)` — business rejection. Infrastructure faults still throw.
- **`EdictRejectionReason(string Code, string Message)`** — `Code` is stable and machine-branchable; `Message` is human display text.
- **`IEdictSender.SendAsync(EdictCommand) → Task<EdictCommandResult>`** (`Edict.Contracts.Sending`) — the only dispatch surface. `Edict.Testing` swaps this seam for an in-memory implementation.

A server-side `FluentValidation.IValidator<TCommand>` registered in DI runs as a pre-`HandleAsync` precondition gate; on failure the framework short-circuits to `Rejected` with each `ValidationFailure.ErrorCode` as a `EdictRejectionReason.Code`. The validator never mutates state.

## State and persistence

A Command Handler owns durable aggregate `State` on `EdictCommandHandler<TState>`. When that `State` is saved follows one rule: **what a completing `HandleAsync` did, happens.**

- **`State` persists whenever `HandleAsync` completes** — on both `Accepted` and `Rejected`, and independent of whether the handler raised an Event. The `EdictCommandResult` is the caller's answer only; it gates nothing. A handler that mutates `State` and raises no Event still has its mutation committed, so the "Command A accumulates state, Command B later acts on it" pattern works.
- **Raised Events publish based on `Raise`, not the result.** If the handler calls `Raise` and then returns `Rejected`, the Event still publishes — `Raise` is imperative, and the result does not retroactively gate it. Conversely a handler that raises nothing publishes nothing, regardless of outcome.
- **A throw is the only discard path.** If `HandleAsync` throws, the partial `State` mutation is rolled back by reloading the last durable snapshot within the same turn, the buffered Events are dropped, and the exception propagates back to the caller of `SendAsync` — a Command's handler runs as a direct grain call, so its throw surfaces to the sender rather than dead-lettering (dead-letter is for the asynchronous Outbox effects, not the handler body). Treat `Rejected` as "no, and here is why" — not as a rollback. Only a throw rolls back, and a reject is not a throw.
- **A Command Validator rejection writes nothing.** It short-circuits before `HandleAsync` runs, so there is no `State` change and no Event to discard.

```csharp
public partial class CartCommandHandler : EdictCommandHandler<CartState>
{
    // Mutates State, raises no Event: the accumulated basket survives deactivation.
    public Task<EdictCommandResult> HandleAsync(AddItemToCartCommand command)
    {
        State.Skus.Add(command.Sku);
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    // Reads the state the earlier Commands accumulated, then raises one Event.
    public Task<EdictCommandResult> HandleAsync(CheckoutCartCommand command)
    {
        Raise(new CartCheckedOutEvent(command.CartId, State.Skus.Count, State.Skus.ToArray()));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}
```

## Analyzer rules

- **EDICT003** — concrete commands must have exactly one `[EdictRouteKey]` property, and that property must be of type `Guid`.
- **EDICT004** — a given concrete command type can be the parameter of at most one `HandleAsync` across all command handlers (compilation-end check).
- **EDICT006** — concrete commands must be declared `partial`; the generator emits the Orleans `[Alias]` into a second partial declaration.
- **EDICT015** — call `IEdictSender.SendAsync` with a concrete-typed argument, not an `EdictCommand`-typed variable; the interceptor fast path needs the static type to intercept the call site.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `EdictCommand`, `RouteKey`, `Command Result`, `Command Validator`, `Sender`.
- Concepts — [validators.md](validators.md), [events.md](events.md), [sagas.md](sagas.md), [telemetry.md](telemetry.md).
- Decision trail — [ADR-0055](../../adr/0055-command-handler-state-persists-on-completion.md) (state persists on completion, independent of events).
