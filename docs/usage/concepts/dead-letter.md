# Dead letter

Dead-letter is the forensic-only tail of the Outbox. An outbox effect that exhausts `MaxAttempts` is recorded as a row on a fleet-wide table-projection — not blocked, not retried automatically, not redriven. The dead-letter mechanism is observability, not back-pressure.

There is **no `RedriveAsync`**. Recovery is manual: re-emit the source command or saga step for `PublishEvent` and `SendCommand`; repair the destination row by hand for `UpsertRow`, informed by the dead-letter row's contents.

```csharp
IReadOnlyList<EdictDeadLetterEntry> entries =
    await deadLetterRepository.ListAsync(grainKey: orderId.ToString());

foreach (EdictDeadLetterEntry entry in entries)
{
    // entry.Kind, entry.EffectTarget, entry.AttemptCount, entry.Reason,
    // entry.PayloadJson, entry.FailureKind, entry.TraceParent, ...
}
```

For fleet-wide triage during a system-wide failure, use `ListAllAsync()` instead — the projection collapses every dead-letter row into one partition for cheap fleet-wide reads.

## Surface

- **`IEdictDeadLetterRepository`** (`Edict.Contracts.DeadLetter`) — **strictly read-only**:
  - `ListAsync(string grainKey, CancellationToken)` — dead letters for a single source aggregate.
  - `ListAllAsync(CancellationToken)` — every row in the fleet-wide partition.
- **`EdictDeadLetterEntry`** (`Edict.Contracts.DeadLetter`) — the projection row. Carries `EntryId`, `Kind` (`PublishEvent` / `SendCommand` / `UpsertRow` / `InvokeHandler`), `AttemptCount`, `DeadLetteredAt`, `SourceGrainKey`, `SourceGrainType`, `EffectTarget` (encoded per kind — `"{stream}/{eventType}"` for `PublishEvent`, `"{targetGrainType}/{targetGrainKey}"` for `SendCommand`, `"{table}/{pk}/{rk}"` for `UpsertRow`), `TraceParent`, `CorrelationId`, `ExceptionType`, `Reason`, `PayloadJson`, `SourceEventType`, `SourceEventId`, and `FailureKind`.
- **`EdictDeadLetterFailureKind`** (`Edict.Contracts.DeadLetter`) — discriminator:
  - `EffectFailure` (default) — an outbound effect exhausted `MaxAttempts` at the publisher.
  - `BlobMissing` — a receiver could not materialise a claim-checked event because its blob had been reaped. `SourceEventId` is the locator for the (now-gone) parked body — the claim-check key is the event's `EventId`, so the same id that identifies the event also locates its blob.
- **`EdictDeadLetterProjectionBuilder`** is auto-wired by `AddEdict()` as a fleet-wide singleton — consumers do not register or implement it.

## How a dead letter lands

1. An outbox effect throws repeatedly and reaches `EdictOptions.OutboxMaxAttempts`.
2. In the same one grain-state write, the engine removes the failing entry from the Outbox slice and appends a new `PublishEvent` entry carrying an `EdictDeadLetterRaised` event.
3. The auto-wired `EdictDeadLetterProjectionBuilder` consumes that stream and upserts the row.
4. Operators read via `IEdictDeadLetterRepository`. The captured `TraceParent` lands on the row so trace continuity from the original effect is preserved, and the row's `CorrelationId` is the chain-stable id of the work that failed — every dead-letter row from one causal chain shares it, so a fleet-wide listing groups by `CorrelationId` to see all the failures one Command set in motion, where `TraceParent` is null under unsampled traces. The facade is read-only and routes through the dead-letter projection grain (the same read path as any List projection), not the store directly.

## Saga-lifecycle dead letters

Two dead-letter causes do not come from an outbox effect exhausting `MaxAttempts`; they are promoted directly by a saga's lifecycle and land on the same projection:

- **`EdictSagaTimeoutException`** (`edict.dead_letter.failure_reason = SagaTimeout`) — a saga's absolute lifetime cap fired and `OnSagaTimeoutAsync()` was **not** overridden, so the default routed the fired cap to dead-letter. The fix is on the saga: either give it an `OnSagaTimeoutAsync()` that compensates, or, if the workflow genuinely should run longer, raise its `[EdictSagaTimeout]` or declare it `Unbounded`.
- **`EdictSagaTerminalException`** (`edict.dead_letter.failure_reason = SagaTerminal`) — a genuinely-new Event of a type the saga *handles* arrived at a saga that had already become terminal (`Completed` via `Complete()`, or `TimedOut`). An at-least-once redelivery of an already-handled Event still dedups silently, and an unrelated event type the saga receives off its shared stream is ignored; this row means a *new, handled* Event reached a finished saga, which usually points at an event the workflow did not expect to outlive `Complete()`.

Both reasons are distinct from `ConsumerBug` so the two saga-lifecycle failures read apart from a thrown `HandleAsync`. See [sagas.md](sagas.md) for the lifecycle and ADR-0050 for the rationale.

## Per-substrate naming

The dead-letter projection writes to a literal table named `"deadletter"`, partition-collapsed into one fleet-wide partition for cheap query. The physical artefact under that name follows the persistence substrate:

- **Azure Table Storage** — table `deadletter` in the configured storage account.
- **Postgres** — `deadletter` projection table in the Edict schema.

The substrate is decided by the persistence wiring, not by the dead-letter mechanism. See the wiring page for your persistence substrate.

## Analyzer rules

None — dead-lettering is a runtime promotion driven by `MaxAttempts` exhaustion.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Dead Letter`, `Outbox`, `Claim Check`.
- Configuration — [core.md](../../configuration/core.md) — the canonical reference for the `Outbox*` retry knobs (`OutboxMaxAttempts`, `OutboxBaseDelay`, `OutboxMaxDelay`, `OutboxJitterFraction`) that govern when an entry is promoted to dead-letter.
- Concepts — [claim-check.md](claim-check.md), [event-handlers.md](event-handlers.md), [sagas.md](sagas.md), [table-projections.md](table-projections.md), [telemetry.md](telemetry.md).
- ADR — [0018 — Dead letter (forensic-only, table-projection-backed)](../../adr/0018-dead-letter-forensic-only.md).
