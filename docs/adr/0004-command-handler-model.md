# Command handler model

A Command is an immutable `record` deriving from base `Command` (which carries only a framework-assigned `CommandId` — no trace fields, because a direct grain call propagates `Activity` context natively, unlike the stream hop of ADR 0003). Each concrete command marks exactly one `Guid` property with `[RouteKey]`; that Guid is the aggregate grain's key and, when the handler raises events, the event stream's `sourceAggregateGuid` — one correlation id flowing command → grain → event → handler. The aggregate **is** the command handler grain (Guid-keyed, one grain type per aggregate handling many command types); the consumer writes one strongly-typed `Handle(TCommand)` returning `Task<CommandResult>` per command on a `partial` grain, and a source generator emits the Orleans interface, the single `Dispatch(Command)` entry point, the telemetry spans, and the typed sender. Business outcomes are a closed `CommandResult` hierarchy (`Accepted` | `Rejected(IReadOnlyList<RejectionReason>)`, `RejectionReason(Code, Message)`); thrown exceptions are reserved for infrastructure faults only.

## Considered Options

- **A distinct "create" path returning the new id** — rejected: creation routes identically (caller mints the Guid; Orleans virtual grains make the not-yet-activated aggregate addressable). A second routing path would break "a command is a call to the aggregate it names" and muddy the Verify timeline.
- **`Task<T>` arbitrary return payload** — rejected: lets consumers query the write side through the command, collapsing CQRS and making the Event-vs-return distinction ambiguous in snapshots.
- **Exception-based business rejection** — rejected: marshalling an exception across the Orleans grain boundary for an *expected* outcome (e.g. out of stock) is hot-path overhead and exception-as-control-flow.
- **`[Key]` attribute name** — rejected: collides with `System.ComponentModel.DataAnnotations.KeyAttribute`, and this codebase forbids namespace-qualified inline types, so the collision would force `using` aliases on the framework's most basic attribute. Chose `[RouteKey]`.
- **Bare `string` rejection reasons** — rejected: forces consumers to parse display text for programmatic handling; `string → RejectionReason` would be a breaking change in a shipped framework, so the structured form is chosen up front.
- **Non-Guid route keys** — rejected: Orleans allows string/long keys, but the event stream address is `(eventTypeName, sourceAggregateGuid)`; a non-Guid command key breaks the single-correlation-id spine the instant the handler raises an event.

## Consequences

- The generated grain interface is "untyped" (`Dispatch(Command)`); type safety lives on the consumer's `Handle` overloads, not the marshalling surface. Acceptable — no human authors or reads that interface.
- An analyzer must error on: non-`partial` grain; `Handle` not returning `Task<CommandResult>`; zero/multiple `[RouteKey]` or a non-Guid `[RouteKey]`; two grain types claiming the same command (ambiguous routing must die at compile time).
- Consumers must understand the two-channel failure split: `Rejected` for business, exception for infrastructure.
