# Theorycraft: v1.0.0 release readiness — what to do before the freeze

**Date:** 2026-06-02
**Origin:** Deep-dive analysis of the README "What's next" roadmap (`README.md:155-162`), one subagent per feature, each answering a single question with rigor: *if we ship v1.0.0 without this, is adding it later a breaking change, and what surface-shaping must happen now to keep deferral safe?*

**Framing (the maintainer's, verbatim intent):** a 1.0.0 is a semver commitment. The question is not "what is the smallest change" but "what locks in public/persisted surface I'd later have to break." Deferring an *additive* feature is correct; carrying a *present-tense inconsistency* into the freeze is debt. Pre-release context: no consumers, no released data, no compat constraints on data at rest (see memory `edict-prerelease-no-consumers`).

---

## Must address before v1 (at-a-glance)

These are **genuine present-tense debt**, wrong in the tree today, that a 1.0.0 freeze would canonize. They are independent of whether the related roadmap feature is ever built.

| # | Issue | Where | Why it must be done now | Breaking later? |
|---|---|---|---|---|
| 1 | **Dead-letter fault classifier hard-codes Postgres** | `Edict.Core/DeadLetter/DeadLetterFailureClassifier.cs` — `IsPostgresDriverFault()` string-sniffs `"Npgsql"`/`"Postgres"` | Core name-matches a provider it cannot reference. Every future substrate (DynamoDB/Cosmos/NATS/Mongo) silently falls to the `Unhandled` bucket until someone edits Core again. This is the single clearest falsifier of the README's "no framework changes needed" claim. Fix = registered `IEnumerable<IDeadLetterFaultClassifier>` providers contribute to. | Internal mechanism, so not a *public* break later — but it is the recurring "edit Core per substrate" tax. Fix the mechanism, don't add another string match. |
| 2 | **Claim-check key contract is unwritten and already divergent** | `IEdictClaimCheckStore` (`Edict.Contracts/ClaimCheck/IEdictClaimCheckStore.cs`, `internal`); Azure mints `yyyy/MM/dd/{guid:N}` (path), Postgres mints bare `{guid:N}` and **actively rejects** non-GUID keys as `KeyMalformed` (`Edict.Postgres/ClaimCheck/PostgresClaimCheckStore.cs` `GetAsync`) | The seam returns an opaque `string` but the two providers disagree on shape, and `EdictClaimCheckFetchException.Reason.KeyMalformed` bakes in "malformed = not a GUID" — a Postgres-only truth. Decide now: declare the key truly opaque (drop the GUID assumption from Postgres + the KeyMalformed classification) **or** formalize a key format. | `internal` today → free to fix now, **unbounded cost later** if the interface is ever surfaced. The GUID-N trap is documented in memory `claimcheck-classify-parity-271`. |
| 3 | **README "no framework changes needed" is not credible** | `README.md:162` | Both existing non-Azure substrates forced framework-internal concessions (see Appendix). Reword to the truth once #1 lands, e.g. "no public-API changes; provider-specific fault classification is a registered extension point." | Doc honesty, not surface. Blocked on #1 being true. |

### Cheap hedges (optional, minutes each — do if convenient, not blockers)

| # | Hedge | Where | Rationale |
|---|---|---|---|
| 4 | **Tenant ADR stub + CONTEXT glossary entry** | `docs/adr/`, `CONTEXT.md` | Commit the *envelope-carry-not-ambient* decision and `EdictTenantId` (`Guid`-backed) wire shape now (no runtime code). Add one line to the dead-letter ADR (0018) pre-acknowledging the fleet-wide poison read may later become a privileged cross-tenant path — so the eventual change reads as planned evolution, not a 1.x behavior break. |

### Explicit do-NOT-regress (preserve, don't change)

- **Keep `IOutboxEffectExecutor.ExecuteAsync` returning `Task<OutboxEntry?>`** (`Edict.Core/Outbox/IOutboxEffectExecutor.cs`). Today only `InvokeHandler` uses the staged-follow-up return; do not "simplify" it to `Task`. It is the "do work → stage a follow-up command" seam the external-work primitive will ride on.

---

## Build before v1: committed feature

Read-your-writes moves out of the deferred list. We will ship it before the freeze. It is not present-tense debt (the tree is consistent without it), but it is a feature we want inside the 1.0.0 surface rather than bolted on later, because the return shape it rides home in (`EdictCommandResult.Accepted`) is frozen by the semver commitment.

| Feature | What ships | Why pre-v1 | Notes |
|---|---|---|---|
| **Read-your-writes cursor** | An opaque `EdictCursor` returned on `EdictCommandResult.Accepted`, surfacing the `EventId` that already exists at return time but is discarded today (`OutboxHost.EnqueueRaisedEventsAndDrainAsync`). | The cursor lands in a closed record the freeze makes permanent. Shipping now puts the read-your-writes story in 1.0.0 instead of a 1.x addition consumers must feature-detect. `EventId` is already the stable cursor post-ADR-0051, so the value is sitting there unused. | Additive by construction (closed record plus `keyAsPropertyName` MessagePack), so still non-breaking in principle, but deferring means 1.0.0 ships an `Accepted` that throws away the one datum a read-your-writes consumer needs. |

Return `EdictCursor`, not a bare `Guid`, so the cursor can later widen to `(stream, eventId)` without touching the return type (the discipline the former cursor hedge captured). Existing doc: `theorycraft-read-your-writes.md`.

---

## Defer past v1 — confirmed additive, deferral is correct

Building these now would be speculative generality, not debt-avoidance. Each was verified non-breaking to add later.

| Feature | Why deferral is safe | Notes |
|---|---|---|
| **Outbox circuit breaker** | Executor seam (`IOutboxEffectExecutor`) is `internal` (no consumer can implement/close over it); a breaker is decorator/internal-state. Options/metrics/exceptions all additive by construction. | No shaping needed. `theorycraft` n/a. |
| **External-work primitive** | New append-only `OutboxEffectKind` value (guarded by `OutboxEffectKindFrozenOrdinalTests`) + new grain base. `EdictEventHandler` is the existence proof a deferred-exec role bolts on without touching siblings. Envelope evolves additively (`SagaLifecycle` `[Id(3)]`, `EnqueuedAt` `[Id(7)]` precedents). | Most *invasive*, but invasive ≠ breaking. Per-role generator pattern is additive. |
| **Keyed projection builder** | **The feared pre-1.0 rename is already done.** `EdictTableProjectionBuilder<T>` already carries the qualifier; `EdictProjectionBuilder` is already an empty generic marker root. Generator (`EdictTypeClassifier`/`ProjectionDiscovery`) and analyzer (EDICT009) key on the marker, so a sibling is auto-discovered. | Only internal MCP `HandlerRole` gains a `KeyedProjectionBuilder` branch when shipped. Existing doc: `theorycraft-keyed-projection-builder.md`. |
| **Tenant-scoped substrate** | Serialization additive (named-key MessagePack on envelope/messages; `[Id]`-tagged Orleans on persisted state; envelope already widened once for claim-check). The expensive part — re-keying every storage partition — only strands *data at rest*, of which there is none pre-release. | See hedge #4. Existing doc: `theorycraft-tenant-scoped-substrate.md`. The dead-letter `"deadletter"` literal partition (`EdictDeadLetterProjectionBuilder.cs`) is the one surface whose later scoping changes a *documented operator behavior*. |
| **More substrates** | The provider *public* surface is deliberately tiny (`AddEdict*` + options class, per `PublicSurfaceAllowListTests`). Most seams are `internal`/`[EditorBrowsable(Never)]`, so still movable post-1.0 — *provided they stay internal*. | This is where #1 and #2 above come from. See Appendix for the credibility analysis. |

---

## Appendix: evidence the "no framework changes needed" substrate claim is overstated

Both existing non-Azure substrates forced framework-internal concessions when added:

1. **Postgres forced an Edict.Core edit** — the `IsPostgresDriverFault` branch in `DeadLetterFailureClassifier` (issue #1 above).
2. **Kafka forced a whole new test layer** — ADR-0028 §3 records a 4th test layer (adapter-contract tests) that ADR-0024 never anticipated, because Edict now owns the offset-commit silent-failure surface.
3. **Postgres forced `EdictPostgresGrainStorage`** — the end-run around Orleans #9737 (ADR-0029, memory `orleans-adonet-grainstorage-9737-trap`). Any backend whose shipped Orleans grain-storage has a key-model mismatch (DynamoDB single-table, Cosmos partition-key) lands here too.

**Seam freeze-risk ranking** (for whoever picks up substrate work):
- `IEdictTableRepository<T>` / `IEdictTableWriteStore<T>` — well-shaped, two impls, persistence-neutral. **Low risk.**
- `IEdictClaimCheckStore` — **highest risk** (issue #2); saving grace is it's `internal`.
- `IGrainStorage` optimistic-concurrency shape (`(grain_type, grain_id, state_name, service_id)` + `version` ETag) — proven once (Postgres); Cosmos/Dynamo conditional-write semantics differ. Unproven generalization.
- Dead-letter fault classification — guaranteed per-substrate touch until #1 is fixed.

**`ISubstrate` is NOT the production seam** (CONTEXT.md, ADR-0030): it is harness bring-up/tear-down only. The conformance battery proves a new provider passes shared *behavior* scenarios; it does **not** prove the production abstractions are *sufficient* for a different key/partition model.

**Optional de-risking move (maintainer's call):** add ONE structurally-different *persistence* substrate before 1.0.0 — a single-table key-model backend (DynamoDB) or partition-key document store (Cosmos) — **not** another queue. Both current persistence backends are "rich keyed table with arbitrary composite string keys + version ETag"; they do not stress the claim-check opaque-key contract or the grain-storage ETag generalization. A Dynamo/Cosmos provider is the cheapest probe (reuses the conformance battery via a new `ISubstrate` impl, no new adapter-contract test layer) that forces those contracts into the open *while they are still changeable*. If not done, land #1 and #2 at minimum before the freeze.

---

## Suggested skills for the pick-up session

- **`tdd`** — for issues #1 and #2 (both are mechanism changes with clear test targets; the gen/analyzer/classifier code is the top test priority per memory `feedback-gen-analyzer-silent-failure`).
- **`surface-config`** — if #1's classifier registration introduces any tunable.
- **`grill-with-docs`** — before building #1/#2, to settle the claim-check key contract decision (opaque vs formalized) against CONTEXT.md / ADR-0020 / ADR-0042 language. This is a genuine fork, not a foregone conclusion.
- **`to-prd` / `to-issues`** — to slice #1 + #2 (+ optional Dynamo/Cosmos probe) into shippable tracer-bullet work.

## Conventions reminder for the pick-up session

- Commit direct to `main`, trunk-based (memory `feedback-commit-direct-to-main`).
- New framework throws are `Edict*`-typed (memory `edict-exceptions-only-direction`); exception philosophy in CLAUDE.md is the contract.
- Don't run throughput tests in routine runs (memory `dont-run-throughput-tests-by-default`).
