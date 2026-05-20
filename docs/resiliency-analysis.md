# Edict Resiliency Analysis (Framework-Only, Production Lens)

**Status:** Theorycraft / discussion input. Not a decision, not an ADR.
**Scope:** `Edict.Core`, `Edict.Contracts`, `Edict.Telemetry` only — explicitly excluding `Edict.Azure`.
**Workload assumed:** many silo instances in an AKS cluster, sustained ~5 000 commands/events per second, bursts to ~10 000. Pods may die at any moment; downstream APIs degrade for tens of minutes.

The framework leans on three load-bearing primitives:

1. **One atomic grain-document write** — `GrainEnvelope { Payload, Outbox, Idempotency }`.
2. **One Outbox engine** — FIFO + stop-at-head + lazy Reminder + dead-letter-as-tail-promotion.
3. **Per-consumer dedup window** — bounded window of recently handled `EventId`s.

This document inventories what Orleans gives us for free, where Edict's own design is genuinely strong, and where the seams crack under production load and schema drift.

---

## 1. What Orleans gives the framework for free

| Orleans capability | What it buys Edict | Production implication |
|---|---|---|
| Single-writer turn discipline per grain key | Atomic `GrainEnvelope` write is *real*, not aspirational | No two pods can race the same aggregate's Outbox state |
| Activation placement / balancing | 10 k EPS spreads across silos by `(grainType, key)` | No node-local queue; dedup is per-grain, no cross-pod coordination |
| Stream pulling agents | Backpressure on the stream-provider side | Bursts smear over consumers without losing events |
| Implicit subscriptions | New pods don't have to "discover" subscribers | Consumers activate on first event for the route key |
| Persistent reminders | Crash-recovery timer that outlives the silo that registered it | Drain heartbeat survives pod death |
| Serializer manifest with `[Alias]` | Type identity decoupled from CLR name | Schema-drift story is largely Orleans, *provided* `ORLEANS0010` is never suppressed (it isn't) |

---

## 2. Where Edict itself is genuinely strong

| Property | Mechanism | File / location |
|---|---|---|
| Atomicity is real, not best-effort | `EnqueueAndDrainAsync` commits `{Payload, Outbox, Idempotency}` in one `WriteStateAsync` *before* draining | `Edict.Core/Outbox/OutboxHost.cs:104` |
| Crash recovery is automatic | `OnActivateAsync` drains anything left from a prior pod before serving new traffic | `Edict.Core/Outbox/OutboxHost.cs:79` |
| Steady state is zero reminders | Lazy register-when-dirty / unregister-on-drain | `Edict.Core/Outbox/OutboxHost.cs:175` |
| Backoff cannot stampede | Deterministic per-entry jitter from `EntryId` hash; no RNG, no clock | `Edict.Core/Outbox/OutboxBackoff.cs` |
| Poison effects self-heal the tail | Dead-letter-as-tail-promotion: failed head leaves the FIFO atomically with the notification append | `Edict.Core/Outbox/OutboxHost.cs:148`, `OutboxSlice.PromoteHead` |
| Trace context survives async hops | W3C `traceparent` captured per entry, restored as **parent** (never a span link) at drain | ADR 0003, `Edict.Core/Outbox/PublishEventExecutor.cs:31` |

---

## 3. Concerns — graded by severity

### 3.1 Concern summary table

| # | Concern | Severity | Surface | Recovery posture |
|---|---|---|---|---|
| 1 | ~~Head-of-line blocking inside one aggregate~~ **Resolved by [ADR 0026](adr/0026-per-entry-independent-retry-unified-deferred-dispatch.md)** | ~~High~~ | ~~OutboxHost FIFO stop-at-head~~ — replaced by per-entry independent retry, sequential in insertion order; a failing entry no longer blocks subsequent entries on the same grain | Per-entry backoff + reminder; no aggregate wedge |
| 2 | `OutboxSlice` grows inline in the grain document | High (cap-overflow half resolved by ADR 0025) | `OutboxSlice.Pending : List<OutboxEntry>` | **Cap-overflow half resolved by [ADR 0025](adr/0025-grain-state-on-blob-substrate.md) — grain state on Blob has no row cap.** Observability half (saturation metrics, intake throttle) remains open. |
| 3 | ~~`[Alias]` discipline has a hole at `UpsertRowEffect.RowTypeName`~~ **Resolved by [ADR 0027](adr/0027-attribute-placement-policy-and-persisted-state-marker.md)** | ~~High~~ | ~~`Edict.Core/Outbox/UpsertRowExecutor.cs:40`~~ — `RowTypeName` (AQTN) replaced by `RowAlias` (frozen literal); drain resolves via `Orleans.Serialization.TypeConverter.Parse`; `IEdictPersistedState` + EDICT011 enforce the frozen-literal discipline at compile time | Closed — rename/move is now `[Alias]`-stable |
| 4 | Receiver-side missing-blob dead-letter path is documented but unshipped | Medium | ADR 0024 §"receiver-side failure mode" | A deleted blob wedges consumers indefinitely until shipped |
| 5 | Dedup window is silently bounded | Medium | `EdictIdempotencyBase.WindowSize = 100` (default sourced from `EdictOptions.IdempotencyWindowSize`, ADR 0028) | Singleton consumers are the dangerous default |
| 6 | Route shape changes during rolling deploy | Medium | Saga `SendCommand` entries; new/removed `Handle(TCommand)` | Dead-letter (correct but loud) |
| 7 | Telemetry exporter is a backpressure source | Medium | `EdictDiagnostics.ActivitySource` | Operator must size exporter / sampling |
| 8 | Sampled-out activity ⇒ orphan trace through the drain | Low | `EdictSaga.cs:107`, `EdictEventHandler.cs:84` | Forensic value lost, not data |
| 9 | `WriteStateAsync` mid-drain failure | Low | OutboxHost.cs:111, 153, 157, 162 | Orleans deactivates → reload → re-drain (correct) |
| 10 | Time-provider non-uniformity | Low | `PublishEventExecutor.cs:44` uses `DateTimeOffset.UtcNow` | Tests can't virtualise `OccurredAt` |

### 3.2 High-severity concerns

#### 3.2.1 Head-of-line blocking per aggregate

> **Status:** **resolved by [ADR 0026](adr/0026-per-entry-independent-retry-unified-deferred-dispatch.md)** — FIFO stop-at-head is retracted; drain is per-entry-independent retry, sequential in insertion order. A failing entry bumps its own `NextAttemptUtc` and the drain continues past it; subsequent entries on the same grain are not blocked. The per-aggregate wedge described below cannot occur. Historical context for the original concern is preserved below.

Stop-at-head means a single poison effect blocks every subsequent effect on *that grain key* until either it succeeds or hits `MaxAttempts`. Worst-case wedge with defaults (`MaxAttempts=8`, `MaxDelay=5 min`):

| Knob | Default | Worst-case head wedge |
|---|---|---|
| `MaxAttempts` | 8 | — |
| `BaseDelay` | 2 s | — |
| `MaxDelay` | 5 min | — |
| Aggregate wedge per aggregate | — | ~30–40 min |
| Cluster throughput collapse on a single hot route | — | Tens of minutes during outage |

This is correct by design (per-aggregate causal order), but should be called out explicitly in operator docs. Tuning:

| Tuning goal | Suggested setting |
|---|---|
| Favour throughput, accept noisy dead-letters | Low `MaxAttempts`, low `MaxDelay` |
| Favour RCA fidelity, accept long wedges | High `MaxAttempts`, high `MaxDelay` |

#### 3.2.2 `OutboxSlice` is a `List<OutboxEntry>` on the grain document

> **Status:** cap-overflow half **resolved by [ADR 0025](adr/0025-grain-state-on-blob-substrate.md)** — grain state moves uniformly to Azure Blob storage; Blob has no per-property cap and no row cap, so the failure mode described below cannot occur. The *observability* half (no saturation metric, no intake throttle when a backlog forms) remains open for a future ADR — a 200-deep `OutboxSlice` is still operationally suspect regardless of substrate.

`Edict.Core/Outbox/OutboxSlice.cs:21` — pending entries are persisted inline in the envelope. Under burst load with slow downstreams, a single aggregate accumulates:

`grain document size ≈ payload + dedup window + Σ(entry overhead + serialised event)`

Claim-check shrinks the *event payload* but not the per-entry overhead × N. The 32 KB-per-property cap (and 1 MB row cap on Azure Table) is reachable before claim-check helps.

| Mitigation | Trade-off |
|---|---|
| Per-aggregate pending-entries soft cap → throw `EdictOutboxSaturatedException` at intake | Re-introduces ADR-0019's intake block that 0022 removed; operator-unfriendly but row-safe |
| `edict.outbox.pending.{count,bytes}` metric per grain | Observability only; doesn't prevent overflow |
| Hard cap `Pending.Count` with FIFO drop-tail | Loses ordering guarantee — probably not acceptable |

#### 3.2.3 `[Alias]` discipline hole at `UpsertRowEffect.RowTypeName`

> **Status:** **resolved by [ADR 0027](adr/0027-attribute-placement-policy-and-persisted-state-marker.md)** — `UpsertRowEffect.RowTypeName` (AQTN, resolved via `Type.GetType`) is replaced by `RowAlias` (the row POCO's frozen `[Alias]` literal, resolved via `Orleans.Serialization.TypeConverter.Parse`). The row POCO implements the new `IEdictPersistedState` marker; the analyzer EDICT011 enforces frozen-literal `[Alias]` + `[GenerateSerializer]` + `[Id(n)]` on every implementor, so the hole closes at compile time, not at incident time. Historical context for the original concern is preserved below.

`Edict.Core/Outbox/UpsertRowExecutor.cs:40` uses `Type.GetType(effect.RowTypeName)`. **`Type.GetType` does not honour `[Alias]`.**

| Refactor on a row POCO | Outbox effect outcome |
|---|---|
| Rename class | All in-flight `UpsertRow` entries → dead-letter forever |
| Move namespace | All in-flight `UpsertRow` entries → dead-letter forever |
| Move assembly | All in-flight `UpsertRow` entries → dead-letter forever |
| Add/remove property | Tolerated by JSON serializer (the row POCO's payload is JSON, not Orleans codec) |

This is the one mechanism the framework *promises* is idempotent and crash-safe; it bypasses Orleans's wire-identity story. Worth designing in an alias-equivalent type token (or AQTN + discovered fallback map) before this bites a consumer mid-incident.

For comparison — what each persisted/wire type uses:

| Type | Identity mechanism | Survives rename? |
|---|---|---|
| `EdictEvent` subclasses on the wire | MessagePack class name (key-as-property) | Frozen by convention |
| `EdictCommand` subclasses on the wire | `[Alias(nameof(TheCommand))]` (ADR 0010) | Yes |
| `GrainEnvelope<TPayload>` in storage | `[Alias("GrainEnvelope\`1")]` (frozen literal, ADR 0017) | Yes |
| `OutboxSlice`, `OutboxEntry`, `IdempotencyState` | Frozen literal `[Alias]` | Yes |
| `EdictDeadLetterRaised.PayloadJson` | System.Text.Json over the producer's concrete type | Forensic only — fine |
| **`UpsertRowEffect.RowTypeName`** | **`Type.GetType(AQTN)`** | **No** |

### 3.3 Medium-severity concerns

#### 3.3.1 Receiver-side blob-missing dead-letter path

ADR 0024 specifies a receiver-side dead-letter promotion when a claim-check blob fetch fails after `MaxAttempts` — synthetic `EdictEventBlobMissing` into the same fleet-wide dead-letter projection. **Documented but not yet implemented.** Until it lands, a deleted/lifecycle-reaped blob will sit in Orleans' stream-retry forever with no operator surface beyond Orleans logs.

#### 3.3.2 Dedup window is silently bounded

`EdictIdempotencyBase.WindowSize = 100` (`Edict.Core/Idempotency/EdictIdempotencyBase.cs:64`). ADR 0028 surfaces the silo-wide default as `EdictOptions.IdempotencyWindowSize`; the per-grain-type `protected virtual int WindowSize` override remains for high-EPS singletons. The silent-failure half (no overwrite metric, no analyzer error) is unaddressed.

| Consumer shape | EPS at consumer | Window exhaustion time | Risk |
|---|---|---|---|
| Per-aggregate consumer (normal) | ~1–10 | hours | None |
| Per-aggregate hot key | ~100 | ~1 s | Low |
| **Fixed-Guid singleton consumer** | **5 000** | **~20 ms** | **A redelivery older than 20 ms looks like a new event** |

There is no metric, no warning, no analyzer error if `WindowSize` is too small relative to the redelivery window of the underlying stream provider. Suggested mitigations:

- `edict.idempotency.window.overwrite_per_minute` counter — a non-zero value means redelivery beyond the window is possible.
- ADR / docs note that singleton consumers must explicitly size `WindowSize`.

#### 3.3.3 Route shape changes during rolling deploy

Adding or removing a `Handle(TCommand)` overload between v1 and v2 of a Command Handler. A `SendCommand` Outbox entry staged on v1 may drain on v2 with no matching route → throws → dead-letter. Correct, but a *permanent* loss surface for in-flight commands during a rolling deploy. Worth one explicit test in the conformance harness:

| Scenario | Expected | Currently tested? |
|---|---|---|
| v1 stages `SendCommand(X)`, v2 has no route for X | Dead-letter the entry, log clearly | Implicit (general dead-letter path); no targeted test |
| v1 stages `SendCommand(X)`, v2's `X` is renamed but `[Alias]` preserved | Drain succeeds | Implicit |
| v1 stages `UpsertRow` for row type R, v2's R moved namespace | Dead-letter the entry (today); should be alias-stable (future) | Not tested |

#### 3.3.4 Telemetry exporter as a backpressure source

At 5–10 k EPS, `Activity.Current` chains and exporter queueing are non-trivial. If `EdictDiagnostics.ActivitySource` is 100 % sampled and the OTLP collector backpressures, the framework keeps creating `Activity` objects that pile up in the exporter's queue. The framework should document the recommended sampling at this throughput.

### 3.4 Low-severity concerns

#### 3.4.1 Sampled-out activity ⇒ orphan trace through the drain

`EdictSaga.cs:107`, `EdictEventHandler.cs:84` capture `Activity.Current?.TraceId.ToHexString()`. If the originating activity is sampled out, `Activity.Current` is null and the entry has `TraceParent=null`. At 1 % sampling, most outbox entries lose causality through the deferred drain — an operator investigating the dead-letter projection can't follow a trace back if the inciting trace wasn't sampled.

#### 3.4.2 `WriteStateAsync` failure mid-drain

Every call site assumes the write succeeds. An Azure Table 503 throws out of `DrainAsync`; Orleans deactivates the grain on uncaught exceptions; the next activation reloads persisted state and re-drains. Correct — but worth one targeted test ("WriteStateAsync throws mid-drain → grain reactivates with pre-throw state intact") rather than assume the composition refactor (#69) preserved it.

#### 3.4.3 Time-provider non-uniformity

`Edict.Core/Outbox/PublishEventExecutor.cs:44` stamps `OccurredAt = DateTimeOffset.UtcNow`, bypassing the injected `TimeProvider`. Production-correct, test-fragile.

---

## 4. Concerns you may not have considered

| Concern | Why it matters at 5–10 k EPS |
|---|---|
| **Reminder service throughput.** Orleans reminder service is a cluster-wide table (one row per reminder). | During a fleet-wide downstream outage, every affected aggregate registers a drain reminder simultaneously. 100 k aggregates → 100 k reminder-table rows churning at the 1-minute period. Document the reminder-table sizing assumption. The 1-minute floor is now tunable as `EdictOptions.OutboxDrainReminderPeriod` (ADR 0028). |
| **`OutboxSlice.Enqueue` is O(n).** `[.. Pending, entry]` allocates a new list. | Fine at small depths; at 50+ pending × burst rate, per-grain CPU starts to show. Persistence cost dominates anyway — flag if `Pending.Count > ~20`. |
| **Implicit subscription + new consumer deploy.** A new `EdictEventHandler` doesn't see in-flight events on the stream. | Expected (ADR 0001) but worth one operator-doc line: "a new consumer starts from now; backfill is the consumer's design problem." |
| **Saga single-command rule is a runtime throw.** Bug fires `Dispatch` twice → runtime exception → dead-letter promotion. | At burst load a saga bug that fires twice 1 % of the time fills the dead-letter projection with stack traces. Worth re-evaluating "no analyzer" given the cost at scale. |

---

## 5. Headline summary

The atomic-envelope design and FIFO / stop-at-head / lazy-reminder shape are the right primitives for this workload. The framework's resiliency is well-thought-through and Orleans is correctly leaned-on.

### Priority order for follow-up

| Priority | Item | Rationale |
|---|---|---|
| 1 | ~~`UpsertRowEffect.RowTypeName` uses `Type.GetType` and bypasses `[Alias]`~~ **Resolved by [ADR 0027](adr/0027-attribute-placement-policy-and-persisted-state-marker.md)** | ~~The framework's most important schema-drift guarantee has a hole~~. Closed: `RowAlias` replaces `RowTypeName`; `IEdictPersistedState` + EDICT011 enforce the discipline at compile time |
| 2 | ~~Grain-document size envelope under sustained burst~~ Observability for `OutboxSlice` backlog under sustained burst | ~~Without an intake throttle or saturation metric, an aggregate caught behind a slow downstream during a burst will overflow the row before claim-check helps~~. **Cap-overflow half resolved by [ADR 0025](adr/0025-grain-state-on-blob-substrate.md).** Observability half (saturation metrics, intake throttle) remains open — a deep backlog is still operationally suspect even when the substrate can hold it. |
| 3 | Receiver-side blob-missing dead-letter path (ADR 0024) | Documented but unshipped; until it lands, claim-check'd consumers are wedge-able by retention misconfiguration |
| 4 | Singleton consumer `WindowSize` default of 100 — **option surfaced by [ADR 0028](adr/0028-config-surface-and-installation.md)** as `EdictOptions.IdempotencyWindowSize` (silo-wide default) with the per-grain-type `protected virtual int WindowSize` override retained for high-EPS singletons; the silent-failure half (no overwrite metric, no analyzer error) remains open | Cheap fix with a metric + docs note |
| 5 | Reminder-table sizing & ~~1-minute floor~~ under fleet-wide outage — **tunable-floor half resolved by [ADR 0028](adr/0028-config-surface-and-installation.md)** (`OutboxDrainReminderPeriod` on `EdictOptions`); sizing-assumption half remains open as an operator-docs concern | Operator-doc concern only |
