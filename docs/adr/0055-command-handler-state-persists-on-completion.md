---
status: accepted
---

# Command-handler state persists on completion, independent of events

Previously a Command Handler's durable `State` was written only as part of committing raised events: the single `{ Payload, Outbox }` write happened inside the raise-and-drain path, which early-returned when no event was raised. A handler that mutated `State` and raised nothing therefore lost the mutation on deactivation — making the "Command A mutates state, Command B later acts on that state" pattern silently impossible.

We decided that a Command Handler's `State` persists whenever `HandleAsync` **completes**, independent of whether it raised an Event. The `EdictCommandResult` envelope (`Accepted`/`Rejected`) is the caller's answer only and gates nothing: a completing handler's `State` mutations are committed and its raised Events published on **both** outcomes. A handler that **throws** is the sole discard path — its partial `State` mutation is rolled back by reloading the last durable snapshot (`ReadStateAsync`) within the same turn and the buffered events are dropped, then the exception propagates. A Command Validator rejection short-circuits before `HandleAsync` runs, so it writes nothing.

## Considered options

- **Explicit opt-in marker** (consumer calls `Persist()`/`MarkChanged()`) — rejected: re-introduces the exact footgun being fixed (forget the call, lose the state), silently.
- **Dirty-tracking via snapshot comparison** — rejected: requires deep equality over arbitrary consumer POCOs; fragile and costly.
- **Result gates events** (`Raise` then `Rejected` discards the event) — rejected: a hidden rule contradicting the consumer's imperative `Raise`; events follow the same "what the handler did, happens" rule as state.

## Consequences

- The commit/drain/reload lifecycle moves out of the generated dispatch spine into the hand-written `ValidateAndHandleAsync` in `Edict.Core`, so it is unit-testable without the generator; the generated spine collapses to a trivial type-switch (forces a generator snapshot regen).
- A durable write now occurs on every completing command, including no-op and `Rejected` paths that changed nothing — an accepted cost in exchange for a footgun-free model. A future dirty-tracking optimization could revisit this without changing the observable contract.
- Supersedes-in-part the earlier implication (ADR-0015) that the state write is tied to committing outbox effects: state commits even when the Outbox gains no entry.
