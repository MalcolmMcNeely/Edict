# Saga model: generic idempotency base, one-command-per-event hard limit

**Status:** accepted — depends on ADR 0018 (the Outbox carries the saga's `SendCommand` effect); keeps ADR 0017 brand clause (b) literally true.

A saga coordinates a multi-step workflow: it reacts to Events and issues Commands, holding durable progress. `CONTEXT.md` and ADR 0017's brand clause (b) assert that Event Handlers, Sagas, and Projection Builders all inherit the shared root `EdictIdempotencyBase` — that shared-root fact is the *sole justification* for `EdictIdempotencyBase` keeping the `Edict` prefix. A saga also needs durable progress state plus an outbound-command buffer, which the fixed `IdempotencyState` (ring only) cannot hold.

We **generic-ify the root**: `EdictIdempotencyBase<TPayload>` over the document `{ Ring, Outbox, DeadLetter, TPayload }`. `TPayload` is `Unit` for Event Handlers and Projection Builders, `TProgress` for `EdictSaga<TProgress>`; a non-generic `EdictIdempotencyBase : EdictIdempotencyBase<Unit>` shim keeps payload-free subclasses from sprouting `<Unit>`. The inheritance relationship — and therefore brand clause (b) and the "all three inherit it" statement — stays literally true; the cost is a generic-parameter ripple across the idempotency subclasses, accepted as the price of not carving the saga out as a model wart.

A saga issues **exactly one Command per Event handled**, via `Dispatch(command)` — a single-command API that **throws if called twice within one event handler**. This is a deliberate **hard runtime limit** and a deliberate **asymmetry** with the Command Handler's buffering `Raise` (where "one event per command" is only a soft, code-review-caught simplifying assumption — `CONTEXT.md` flagged-ambiguity). The asymmetry is intentional: a command handler fanning out events is normal; a saga fanning out commands is a coordination smell, and the API *shape* (no list, no buffer) makes the constraint unmissable rather than advisory.

## Considered Options

- **Saga gets its own `Grain<SagaState<TProgress>>`, not inheriting `EdictIdempotencyBase`** — rejected: breaks the documented "all three inherit it" relationship and removes brand clause (b)'s justification; would require an ADR-0017 amendment and leave the model asymmetric.
- **Opaque `byte[]` progress slot in the existing `IdempotencyState`** — rejected: untyped state, off-idiom against the typed-everything style of this codebase.
- **Soft "one command per event" (symmetric with `Raise`)** — rejected: the user requirement was a *limit*; the API-shape hard stop is stronger and self-documenting. An additional `EDICT00x` analyzer was rejected — events deliberately have no such analyzer, and matching that rigor only on sagas would be inconsistent; the runtime throw on a single-command API is sufficient.

## Consequences

- New folder `Saga/` in `Edict.Core`; `EdictSaga<TProgress>` is consumer-facing (brand-prefixed).
- The sample app gains a `Payment` aggregate and an OrderPayment saga: `OrderSubmitted → AuthorizePayment`, `PaymentAuthorized → ConfirmOrder`, `PaymentDeclined → CancelOrder` (the compensation branch) — demonstrating cross-aggregate coordination, re-keying, durable progress, and the one-command-per-event limit naturally.
- The shipped Test Framework timeline surfaces saga progress and dispatched commands; per ADR 0016 the saga's crash/redelivery realism is proven in `Edict.Azure.Tests`.
