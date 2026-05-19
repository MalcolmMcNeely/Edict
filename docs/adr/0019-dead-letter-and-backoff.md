# Dead-letter and backoff: the terminal tail of the Outbox

**Status:** accepted — subordinate to ADR 0018 (read 0018 first; this records the dead-letter trade-offs that did not fit cleanly in the engine ADR).

A transactional outbox must answer "what about an entry that never succeeds?" — dead-lettering is intrinsic, not optional. A permanently failing downstream (stream/grain/store down for hours) must not stall an aggregate forever, nor silently drop the effect, nor break the atomicity guarantee the Outbox exists for.

An entry that fails is retried with **exponential backoff** — each entry carries `AttemptCount` and `NextAttemptUtc` (a pure function of `AttemptCount`); the drain skips entries whose `NextAttemptUtc` is in the future. This reuses the *same* lazy Reminder from ADR 0018 (the gate is just a timestamp check) — no second scheduling primitive. Only a *permanently* failing entry (max attempts exhausted) is **dead-lettered**: moved from the `Outbox` slice to a `DeadLetter` slice **in the same one grain-state write** — the move is atomic by construction; an external dead-letter store would be the two-store gap again. Because FIFO is stop-at-head and a poison head eventually dead-letters (leaving the Outbox), the tail is **self-healing** — head-of-line blocking is bounded, not permanent.

The `DeadLetter` slice is **capped** to keep grain activation small. On overflow the grain **blocks intake**: new commands surface an infrastructure fault, redelivered events are not acked. Nothing is ever silently dropped — consistent with the framework's existing no-silent-loss stance — until ops intervene. Recovery is a **grain method** that atomically redrives an entry (`DeadLetter → Outbox`, attempt counter reset); inspection is a read-only `IEdictDeadLetterRepository` (consumer-facing, `Edict.Contracts`, mirroring `IEdictTableRepository`).

## Considered Options

- **Terminal DLQ with no backoff (fail K times → dead-letter immediately)** — rejected: a multi-hour transient outage would mass-dead-letter healthy entries; backoff drains far fewer false positives.
- **Skip-and-continue on poison** — rejected: silent-ish drop + reorder, contradicts the framework's no-silent-loss contract.
- **External / dedicated dead-letter store or grain** — rejected: the pending→dead-letter move would span two stores, reintroducing the exact non-atomic gap the Outbox closes.
- **Spill DLQ overflow to an external store** — rejected for now: keeps the grain small but recreates the two-store move; block-intake is the simpler stance that loses nothing.
- **Fatal escalate on cap (throw, fault the grain)** — rejected: harsher than block-intake without being safer; block-intake preserves zero-loss and is recoverable.
- **Writable dead-letter repository** — rejected: redrive is a state mutation and belongs on the grain (atomic); the repository stays strictly read-only, mirroring the Table Repository seam split.

## Consequences

- New folder `DeadLetter/` in `Edict.Core` (internal/bare-named); `IEdictDeadLetterRepository` in `Edict.Contracts`, Azure read implementation in `Edict.Azure`.
- Backoff is a pure function — no schedule persisted beyond `AttemptCount`/`NextAttemptUtc` per entry.
- The Test Framework (ADR 0018's virtual clock) makes poison → backoff → dead-letter → redrive unit-testable without real waits; the timeline surfaces dead-letters alongside Commands/Events/Saga state.
