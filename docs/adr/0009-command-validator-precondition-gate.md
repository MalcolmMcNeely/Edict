# Command Validator is a server-side, no-mutation precondition gate

**Status:** accepted

Command validation is a framework layer that runs **server-side, inside the generator-emitted `Dispatch`, before `Handle`, in the same Orleans activation turn**. A validator is a consumer-authored FluentValidation `AbstractValidator<TCommand>` (opt-in per command, resolved from grain DI, no-op when absent). It may read the aggregate's current state but **performs no mutation**; failures map to `RejectionReason`s and return a `Rejected` `CommandResult` — never thrown. It is a *precondition gate*; `Handle` owns the state transition.

## Why

CONTEXT.md fixes rejection as a first-class outcome, never an exception, so validation cannot ride the exception path `CommandSpanScope` re-throws on — it must short-circuit to `Rejected`. Server-side placement makes the gate un-bypassable (the grain is the trust boundary, not the swappable `IEdictSender` seam) and, because validator and `Handle` run in one single-threaded activation turn, the state the validator inspects cannot be raced before `Handle` acts — a correctness guarantee client-side validation cannot offer.

## Considered Options

- **Client-side fail-fast in `Send`** — rejected: avoids a grain activation but lives only in the swappable seam (in-memory test sender can diverge from the real one) and cannot see authoritative current state.
- **Stateless-by-definition validator; anything stateful is `Handle`-only** — rejected: real applications need to reject a command as inadmissible against current state *before* attempting the transition; forcing all of that into `Handle` conflates the precondition with the mutation.
- **State injected via an ambient activation-scoped accessor** — rejected: adds activation-scoped ambient lifetime complexity to otherwise-stateless DI validators.
- **A separate stateful-guard concept distinct from validation** — rejected: a single uniform validation extension point was wanted; a second concept is speculative until genuinely needed.
- **State supplied via FluentValidation `ValidationContext.RootContextData`** — chosen: validators stay stateless DI services, state arrives per-call under an Edict-defined key, and `Edict.Contracts` never needs the consumer's grain-state type.

## Consequences

- The boundary between a Command Validator and a `Handle` rejection is *when they run and whether they mutate*, not structural-vs-business: knowable from current state without attempting the change → validator; only emerges while mutating → `Handle`. Both return the same `Rejected` envelope.
- `Edict.Testing`'s in-memory path must reproduce the same state injection so consumer validators behave identically under test.
- FluentValidation is referenced by `Edict.Core` (executes validators) and the consumer's domain project (authors them); `Edict.Contracts` stays FluentValidation-free.
