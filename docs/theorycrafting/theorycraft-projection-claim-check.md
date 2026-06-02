# Theorycraft — projection claim-check (oversized-row tiering)

**Status:** pre-design / theorycraft. Not a PRD, not a spike, not an ADR. Goal: a fresh session (Claude or human) can pick it up cold and start a design pass without re-deriving the problem statement.

**Sibling doc:** [`theorycraft-read-your-writes.md`](theorycraft-read-your-writes.md). Independent concern; do not bundle.

## The problem

A consumer writes a projection. The projection's row payload is sometimes — or occasionally, or always — larger than the substrate's inline row ceiling.

| Substrate | Inline row ceiling | What "too big" looks like |
|---|---|---|
| Azure Table Storage | 1 MB | Common — any projection that aggregates non-trivial data |
| DynamoDB | 400 KB | Very common — tightest ceiling of any substrate Edict targets |
| MongoDB | 16 MB document | Rare — happens with rendered reports, ML features, embedded media |
| Postgres (bytea / jsonb + TOAST) | ~1 GB practical | Essentially never — TOAST handles it transparently |

Today the consumer has to know which substrate they are on and design their projection shape around the ceiling. That violates Edict's substrate-neutrality promise.

## The pattern Edict already uses for the equivalent event-side problem

The framework already solves this exact shape for **events**:

- Events too large for the queue payload limit get spilled to a blob store
- The queue message carries an address — a "claim check" — pointing at the spilled blob
- The receiver detects the claim-check envelope, fetches the inner payload from the blob store, and presents the original event to the consumer's handler unchanged
- Small events stay inline (conditional wrap, not universal — measured in the consumer-test path)

Reference: claim-check publisher slice (PR #72, follow-up wiring in ADR-0042). The relevant infrastructure already exists: `EdictEventEnvelope` with `Inner` address fields, an `IEdictClaimCheckStore` seam, and `AzureBlobClaimCheckStore` as the Azure implementation.

## The proposed primitive — apply the same pattern to projections

One consumer-facing abstraction: `EdictProjectionBuilder<TRow>`, same as today. No new `BlobProjectionBuilder<T>` parallel surface.

The substrate's projection-storage adapter chooses storage tiering at write time:

1. Serialise the typed row
2. If the serialised payload fits the inline ceiling, write it as a normal projection row
3. If it exceeds the ceiling, write the payload to the substrate-bound claim-check store and persist a sentinel row carrying the blob address
4. On read, the projection accessor detects the sentinel, fetches the blob inline, and returns the typed row to the caller — same as the event-side flow

The consumer's API is unchanged. They write a `TRow`, they read a `TRow`. The substrate decides where it lives.

## Per-substrate implication

| Substrate | Inline ceiling | Spill target | When spill fires |
|---|---|---|---|
| Azure | ~1 MB (Table row, leaving header overhead) | Azure Blob via existing `IEdictClaimCheckStore` | Routinely for nontrivial projections |
| DynamoDB | 400 KB | S3 (would need a new `IEdictClaimCheckStore` impl) | Very routinely |
| MongoDB | 16 MB | GridFS (would need a new `IEdictClaimCheckStore` impl, or external S3) | Rarely |
| Postgres | TOAST handles transparently up to ~1 GB | n/a — no spill needed; adapter implements the seam as a pass-through | Effectively never |

The Postgres case is interesting: the adapter implements the same projection-storage seam but the "spill" branch is a no-op (or dead code). Symmetric API; asymmetric internal implementation. That is already the shape of Edict's conformance battery — every substrate honours the same external contract.

## Why this fits Edict specifically

- **The claim-check pattern is already idiomatic.** Reusing it for projections keeps the framework's mental model coherent for consumers who have already seen it for events.
- **Substrate-neutral consumer API.** The whole point of Edict is consumers write framework code, not substrate-specific code. A `BlobProjectionBuilder<T>` parallel surface would break that promise.
- **Asymmetric internal implementation is already the norm.** Substrate adapters already differ — Azure Table writes one way, Postgres tables another. The projection-storage adapter being asymmetric is no new shape.
- **Existing `IEdictClaimCheckStore` seam is reusable.** The Azure implementation already exists. New substrates that need spill (DynamoDB, MongoDB-with-GridFS) implement the seam; ones that don't (Postgres) skip the spill branch.

## Open design questions

1. **Where does the threshold get configured?**
   - Per-substrate global (e.g. `EdictAzureOptions.ProjectionInlineCeilingBytes`)? — simple, but a consumer with a chatty small-row projection and a rare big-row projection wants different behaviour per type
   - Per-projection-builder type (attribute or options) — more granular but more surface
   - Both, with per-type override — most ergonomic, more wiring
   - Likely answer: per-substrate default + optional per-type override

2. **What does the spill address sentinel look like in the row?**
   The existing event-side `EdictEventEnvelope.Inner` pattern is the obvious model. For projections, the row's substrate representation needs to carry either the typed payload or the claim-check address — discriminated. Mirror the event-side wire shape, do not invent a new one.

3. **What happens on read when the claim-check blob is missing?**
   The event-side pattern (per memory: blob-missing → dead-letter) does not apply here — projections do not have a dead-letter shape today. Options:
   - Throw `EdictClaimCheckMissingException` (consumer sees an exception on read)
   - Return a typed "missing/corrupt" sentinel (consumer has to check)
   - Synthesise a placeholder row with zero fields (silent data corruption — almost certainly wrong)
   Throwing matches Edict's existing exception-policy direction. Caller decides what to do.

4. **Lifecycle and deletion.** When a projection row is overwritten with a smaller payload that no longer needs spill, the old blob is orphaned. Options:
   - Garbage-collect orphans on write (read-before-write to find the old address, delete after)
   - Background sweep keyed off a "last-modified" trail
   - Never delete (storage-cost problem, eventually)
   The first is the safest default; it costs one extra read per write. Configurable per-substrate is probably the right answer.

5. **Should the substrate adapter expose the threshold to the projection builder?**
   Today's projection builder writes a typed row without knowing the substrate. A future consumer might want to introspect "am I about to spill?" for cost-awareness. Probably no — keep the abstraction tight; cost surfaces through metrics, not API.

6. **Should there be a per-row "force inline" / "force spill" opt-out?**
   Strong argument against: violates substrate-neutrality. Strong argument for: occasional consumer use cases (latency-critical reads of small rows). Defer until a real consumer asks.

7. **What is the metric story?**
   Add to the existing `Meter` named `"Edict"`: count of spilled vs inline projection writes, p99 spill payload size, claim-check store latency. Mirror event-side metrics where they exist.

## Constraints from existing decisions

- **Claim-check publisher slice (PR #72) and follow-up ADR-0042.** Established the `EdictEventEnvelope.Inner` shape and the split-extension model (`AddEdictAzureBlobClaimCheck` is its own extension because `Persistence` cannot project-reference `Streaming` without re-dragging the Queues SDK). The projection-side equivalent will probably want the same split.
- **ADR-0002 idempotency model.** Projection-side dedup uses `EventId`. Spill does not change the dedup axis; the sentinel row still carries the metadata needed for dedup.
- **ADR-0007 contracts boundary.** The sentinel row's wire shape crosses the boundary. Reuse the event-envelope pattern; do not invent a new wire concept.
- **Substrate seam (ADR-0030).** Substrate is currently "streaming + state." Projection storage is part of the state axis. Adding a claim-check tier inside state is consistent with the existing seam shape; it does not require a third axis.

## Substrate considerations (detailed)

- **Azure.** Reuse `IEdictClaimCheckStore` / `AzureBlobClaimCheckStore`. The wiring split (per ADR-0042) means projection-storage adapters live in a different extension package than the streaming claim-check. A `AddEdictAzureBlobProjectionClaimCheck` extension is the likely shape — same blob store binding under the hood.
- **DynamoDB.** Needs a new `IEdictClaimCheckStore` implementation against S3 (or another blob store the consumer nominates). 400 KB ceiling means spill is the common path, not the edge case — design accordingly.
- **MongoDB.** Two options: GridFS-backed claim-check store (native Mongo) or external S3. GridFS is the more Mongo-idiomatic answer but adds a second collection axis to the substrate. External S3 is uniform with DynamoDB. Pick during design; no clear winner from here.
- **Postgres.** Adapter implements the seam as pass-through. TOAST + bytea handles arbitrarily large payloads transparently. The spill branch is dead code. Conformance tests for spill still need to run against a synthetic threshold to prove the seam compiles and the round-trip works; in production the threshold is effectively unreachable.

## Failure modes to design for

1. **Spill blob lost / corrupted.** Read throws `EdictClaimCheckMissingException`. Same shape as event side.
2. **Claim-check store unavailable on write.** Projection write fails — must propagate to the outbox/dead-letter flow as a normal write failure, not silently drop the row.
3. **Threshold changes after rows have been written.** Existing spilled rows must still resolve; existing inline rows must still resolve. The adapter must not assume "all rows above the current threshold are spilled."
4. **Concurrent writes overwriting the same key.** One write may inline, another may spill. Last-writer-wins on the sentinel — orphaned blob from the loser needs cleanup (see open question 4).
5. **Sentinel row visible to a projection-reader that does not know about claim-check.** Cannot happen if the substrate adapter is the only writer/reader — but worth a conformance test asserting the encoding is opaque to direct substrate queries.

## Non-goals — out of scope for this primitive

- **"Projection as file."** A projection that *inherently* produces a file (rendered PDF, downloadable CSV, large derived export) is a different consumer concern — the projection's output is a *file*, not a *record that happens to be large*. That probably wants a different API surface (`IProjectionFile<T>` or similar). Flag as a separate theorycraft if it surfaces.
- **Content-addressed projection storage.** Where the projection content's hash is the key and dedup is automatic. Interesting but solves a different problem.
- **Streaming reads of large projections.** A projection row that is 50 MB and needs to stream to the caller rather than load fully into memory. Different abstraction shape; defer.

## Where this lands in the code

Rough sketch — verify against current structure before designing:

- New seam: projection-storage adapter (interface in `Edict.Substrate`, implementations per substrate package)
- `Edict.Azure.Persistence` — new claim-check-aware adapter; new `AddEdictAzureBlobProjectionClaimCheck` extension (or extend the existing claim-check extension; design choice)
- `Edict.Postgres` — pass-through adapter; spill branch is no-op
- New substrate packages (Mongo, DynamoDB) — implement the seam with native blob backings
- `Edict.Contracts` — possible new wire shape for projection sentinel rows; reuse existing envelope pattern
- Conformance battery — large-row round-trip test, per substrate
- Metrics — add spill counters and latencies to the existing `Meter`

## Suggested first slice

Smallest thing that proves the design:

1. Define the projection-storage adapter seam in `Edict.Substrate`
2. Implement the seam in Azure as a thin wrapper around the existing `IEdictClaimCheckStore` — single threshold from options, no per-type override yet
3. Implement the seam in Postgres as pass-through
4. One conformance test: write a 2 MB projection row, read it back unchanged, assert it landed in the claim-check store on Azure and in the row on Postgres (substrate-asymmetric assertion is fine — it is the *external behaviour* — the typed-row round trip — that the conformance contract guarantees)
5. Add spill counter metric

That is enough to validate the seam shape against existing substrates. New substrates (Dynamo, Mongo) pick it up when they ship.

## Related work elsewhere

Worth scanning before designing:

- **AWS S3 Object Pointer pattern** — exactly the same shape, used widely in DynamoDB-backed services
- **Azure Service Bus claim-check pattern** — Microsoft's reference doc on the equivalent message-side pattern
- **EventStoreDB's "linkTo" with external content** — projection-side variant from a different vendor
- **Apache Kafka's tiered storage** — different abstraction shape (transparent inside the substrate) but solving a similar oversized-payload concern
- **MongoDB GridFS docs** — for the GridFS-backed variant if that path is taken
