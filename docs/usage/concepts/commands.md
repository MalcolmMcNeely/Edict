# Commands

An `EdictCommand` expresses intent to change state, routed to one aggregate grain by the single `Guid` property carrying `[EdictRouteKey]`.

```csharp
using Edict.Contracts.Commands;

public sealed partial record PlaceOrderCommand(Guid OrderId, string CustomerReference) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string CustomerReference { get; init; } = CustomerReference;
}
```

Dispatched through the DI-injected `IEdictSender`:

```csharp
EdictCommandResult result = await sender.Send(new PlaceOrderCommand(orderId, "acme/42"));
```

## Surface

- **`EdictCommand`** (`Edict.Contracts.Commands`, abstract record) — carries `CommandId`, framework-assigned. Concrete commands derive as `partial record`.
- **`[EdictRouteKey]`** (`Edict.Contracts.Commands`) — marks the one `Guid` property that selects the aggregate grain. Exactly one per command.
- **`EdictCommandResult`** (`Edict.Contracts.Commands`) — closed hierarchy:
  - `EdictCommandResult.Accepted` — carries no domain data.
  - `EdictCommandResult.Rejected(IReadOnlyList<EdictRejectionReason> Reasons)` — business rejection. Infrastructure faults still throw.
- **`EdictRejectionReason(string Code, string Message)`** — `Code` is stable and machine-branchable; `Message` is human display text.
- **`IEdictSender.Send(EdictCommand) → Task<EdictCommandResult>`** (`Edict.Contracts.Sending`) — the only dispatch surface. `Edict.Testing` swaps this seam for an in-memory implementation.

A server-side `FluentValidation.IValidator<TCommand>` registered in DI runs as a pre-`Handle` precondition gate; on failure the framework short-circuits to `Rejected` with each `ValidationFailure.ErrorCode` as a `EdictRejectionReason.Code`. The validator never mutates state.

## Analyzer rules

- **EDICT003** — concrete commands must have exactly one `[EdictRouteKey]` property, and that property must be of type `Guid`.
- **EDICT004** — a given concrete command type can be the parameter of at most one `Handle` across all command handlers (compilation-end check).
- **EDICT006** — concrete commands must be declared `partial`; the generator emits the Orleans `[Alias]` into a second partial declaration.
- **EDICT015** — call `IEdictSender.Send` with a concrete-typed argument, not an `EdictCommand`-typed variable; the interceptor fast path needs the static type to intercept the call site.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `EdictCommand`, `RouteKey`, `Command Result`, `Command Validator`, `Sender`.
- Concepts — [events.md](events.md), [sagas.md](sagas.md), [telemetry.md](telemetry.md).
