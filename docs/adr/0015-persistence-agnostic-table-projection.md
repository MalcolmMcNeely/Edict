# Table Projection Builder is persistence-agnostic; Azure is one implementation behind a dumb write-store seam

**Status:** accepted — supersedes ADR 0012's placement ("its Azure-Table implementation lives in `Edict.Core`") and the `TypePlacementTests`/`BoundaryTests` rules pinning `AzureTableRepository`/`TableProjectionBuilderGrain` Azure-coupling to Core; the dedup/double-apply/Outbox decisions in ADR 0012 are unchanged

`EdictTableProjectionBuilderGrain` was hard-bound to `Azure.Data.Tables`: it took a `TableServiceClient`, constrained the row to `ITableEntity`, and wrote via `TableClient.UpsertEntityAsync` — only the *read* side (`IEdictTableRepository`) was abstracted. The "Table Projection" mechanism (external composite-key read model so grain activation stays small) is meaningful regardless of backing store, so the Azure binding is removed:

- A **framework-internal write-store seam** is introduced in `Edict.Contracts`, shaped as a *dumb* keyed store: `GetAsync(pk, rk)` + `UpsertAsync(pk, rk, row)`. The grain base **keeps owning** the per-event load→apply→writeback orchestration so it is identical across providers; the store stays trivial to implement.
- The projection row becomes a **plain POCO** (`T : class, new()`) — no storage type, no keys on it. PartitionKey/RowKey travel through the seam as parameters (the grain already computes pk from its primary key and rk via `GetRowKey`), symmetric with the existing read `IEdictTableRepository.GetAsync(pk, rk)`.
- `EdictTableProjectionBuilderGrain` **stays in `Edict.Core`**, depending only on the Contracts seam. The Azure implementation (read repository + write store + POCO↔`TableEntity` mapping) moves to `Edict.Azure`. A future `Edict.DynamoDB` implements the same seam with no new grain base.

## Considered Options

- **Move the grain base into `Edict.Azure` too** — rejected: the mechanism is persistence-agnostic; only the *implementation* is Azure. The base belongs with the other provider-neutral bases in Core.
- **Smart store owning the mutation (`Mutate(pk, rk, apply)`)** — rejected: enables per-provider atomic writes no provider can use yet (ADR 0012 accepts the double-apply gap until the Outbox, which solves atomicity globally), and scatters projection orchestration across every provider.
- **Add write methods to the read `IEdictTableRepository`** — rejected: ADR 0012 makes that seam deliberately read-only and consumer-facing ("the application never writes"); the write seam is a separate, framework-internal concern.
- **Grain reuses the consumer read seam for its load** — rejected: couples runtime-grain correctness to the consumer-facing seam `Edict.Testing` swaps in-memory; a test double could mask real grain behaviour. The write store carries its own `Get`.
- **Defer abstraction (YAGNI until a second provider)** — rejected by the author: doing it now lands the restructure in a coherent end state instead of a half-abstracted one, and makes the multi-provider claim provable.

## Consequences

- `IEdictTableRepository` (read, consumer-facing, `Edict.Contracts`) and the new write-store seam (internal, `Edict.Contracts`) are two distinct seams; the application depends only on the former.
- `BoundaryTests` gains: `Edict.Core` must not depend on `Azure.*`. `TypePlacementTests` for `AzureTableRepository`/table grain are rewritten for the new homes.
- ADR 0012's persisted dedup ring, accepted double-apply gap, and the planned Outbox are all unchanged — the gap is now provider-independent.
- The mechanism battery proving table projection works against a real backend lives in the provider test suite, not Core (ADR 0016).
