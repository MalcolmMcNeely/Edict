# Stateful generic command handler

A Command is an immutable `record` deriving from `EdictCommand`; each concrete command marks exactly one `Guid` property with `[EdictRouteKey]` (the aggregate grain's key); the aggregate **is** the command handler grain, and the consumer writes one strongly-typed `HandleAsync(TCommand)` returning `Task<EdictCommandResult>` per command on a `partial` grain — a source generator emits the Orleans interface, the single `Dispatch(EdictCommand)` entry point, the telemetry spans, and the typed sender. Business outcomes are a closed `EdictCommandResult` hierarchy (`Accepted` | `Rejected(IReadOnlyList<EdictRejectionReason>)`); thrown exceptions are reserved for infrastructure faults only. The base is **stateful and generic** — `EdictCommandHandler<TState>` — persisting an envelope `{ TState aggregate, Outbox, Idempotency }` because the Outbox (ADR 0015) requires the aggregate state and pending effects to commit atomically in one grain-state write; a non-generic `EdictCommandHandler : EdictCommandHandler<EdictUnit>` shim covers the stateless case.

## Considered Options

- **Exception-based business rejection** — rejected: marshalling an exception across the grain boundary for an *expected* outcome (e.g. out of stock) is hot-path overhead and exception-as-control-flow.
- **Stateless handler with a consumer-supplied state hook** — rejected: the framework cannot guarantee atomicity over a write it does not own; the Outbox demands the framework own the state document.
- **Non-Guid route keys** — rejected: event streams address by Guid (ADR 0010), so a non-Guid command key breaks the single-correlation-id spine the instant the handler raises an event.
