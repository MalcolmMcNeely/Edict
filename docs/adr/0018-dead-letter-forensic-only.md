# Dead letter (forensic-only, table-projection-backed)

Dead-lettering is **observability, not back-pressure**: when an Outbox entry exhausts `MaxAttempts`, the engine — in the same one grain-state write — removes the failed entry from the Outbox slice and appends a new `PublishEvent` entry carrying an `EdictDeadLetterRaised` event; a built-in singleton `EdictDeadLetterProjectionBuilder` (auto-wired by `AddEdict`) consumes that stream and upserts to a fleet-wide table with both per-aggregate and fleet-wide queries via the read-only `IEdictDeadLetterRepository`. There is **no cap**, **no in-grain dead-letter slice**, and **no `RedriveAsync`** — recovery is manual re-emission (re-invoke the source aggregate or saga; for `UpsertRow` permanent failure, the operator repairs the row by hand informed by the dead-letter projection). The atomicity guarantee is preserved (the failed-entry removal and the dead-letter publish are the same write); the dead-letter publish inherits the Outbox's at-least-once retry, trace continuity (the captured `traceparent` propagates onto the projection row), and standard window-based idempotency.

## Considered Options

- **Block-intake-on-cap with a per-aggregate `DeadLetter` slice and `RedriveAsync`** (the prior design) — superseded: a long downstream outage stopped the aggregate from accepting commands at all (back-pressure on the producer side of the outage, where the producer cannot do anything about it); every successful command paid the deserialization and write-amplification cost of carrying the slice; inspection was per-aggregate only, but the operator's first triage question during a system-wide failure is "what's broken fleet-wide?".
- **External / dedicated dead-letter store** — rejected: a new transport with its own SDK and retry semantics; the table-projection approach gives a familiar queryable read surface (ADR 0013).
- **Per-aggregate projection + global roll-up at v1** — rejected for v1, kept as the upgrade path: singleton hot-grain risk is bounded by the same Orleans stream provider every other event uses; ship the singleton first, see real consumer load before paying the design cost.
- **Per-effect-kind `MaxAttempts` / backoff** — rejected for v1: one global `MaxAttempts` and one backoff curve via `EdictOptions` matches "knobs with sensible defaults", not knobs per axis.

## Consequences

- Soundness downgrade: ADR 0015's claim that the Outbox closes the ADR-0011 double-apply gap stands **for the transient-failure case only**. Permanent `UpsertRow` failure now leaves the destination row missing and requires manual operator repair.
- The `EdictDeadLetterRaised` event is widened with optional fields populated only for `InvokeHandler` and `BlobMissing` failures (`SourceEventType`, `SourceEventId`, `ClaimCheckKey`, `FailureKind`) so the operator can query the projection without parsing payload bytes.
