# Claim-check key is the event's EventId

The claim-check store is keyed by the event's `EventId`. There is no separately-minted key: `EdictEventEnvelope` carries no pointer field, the dead-letter forensic contracts carry no pointer field, and the `IEdictClaimCheckStore` seam is `Guid`-typed (`PutAsync(Guid eventId, payload)` / `GetAsync(Guid eventId)`). Key-minting has left the store entirely — a store only *encodes* the supplied `EventId` as its own backend key (Azure blob name `eventId.ToString("N")`, Postgres `uuid` `id` column, in-memory dictionary key) and never invents or returns a shape of its own. The `EventId` is assigned once as the event enters the Outbox (ADR-0051), is stable across re-drains, and is already mirrored from the inner event onto the pointer envelope, so the separate pointer was redundant with an identity the envelope already held. The pointer branch is discriminated by `InlinePayload is null` and fetched by `envelope.EventId`; a pointer-branch envelope additionally guards `EventId != Guid.Empty` at construction.

This collapses three separately-carried keys to one identity and re-types the seam so a key-shape disagreement becomes impossible at the type level. It is a public/frozen-surface change to `EdictEventEnvelope`, `EdictDeadLetterEntry`, `EdictDeadLetterRaised`, and `EdictClaimCheckFetchException`, taken now because pre-release carries no compatibility constraint and the asymmetry favours collapsing while it is free: re-adding a pointer field later is additive and non-breaking, whereas removing it later is a break.

## The divergence this closes

The seam's own contract promised the key was opaque — "neither the publisher nor the receiver attempts to derive or interpret the key string" — and that promise was already broken in the tree. Three stores minted three different key shapes, and one parsed the key:

- Azure minted a date-partitioned path `yyyy/MM/dd/{guid:N}` and treated the key as opaque on fetch.
- Postgres minted a bare `{guid:N}` and **actively rejected** any non-GUID key as `KeyMalformed` *before* it queried.
- The in-memory test stores minted a third shape (`edict-claim-check/{guid:N}`) and threw `KeyNotFoundException` on a miss — a different exception type than production, so an in-memory miss dead-lettered as `Unhandled` while production dead-lettered as `Substrate`/`BlobMissing`.

`EdictClaimCheckFetchException.Reason.KeyMalformed` baked a Postgres-only truth ("malformed = not a GUID-N") into the closed dead-letter bucket set, behind a `Reason` enum arm that could fire on no other store. Once the key is a `Guid`, a malformed key is unrepresentable, so the `Reason` enum is gone and `string Key` becomes `Guid EventId`; the type's existence now means exactly "claim-check payload missing." The dead-letter classifier collapses its two claim-check arms to one: `EdictClaimCheckFetchException => Substrate`.

The dead-letter forensic contracts no longer carry a separate pointer. `SourceEventId` is the locator for the (possibly lifecycle-reaped) parked body on a `BlobMissing` row — the same `EventId` that appears in traces and on the dead-letter row, so an operator reconciles one identifier, not two.

## Considered Options

- **Opaque store-minted string** (the prior unwritten default) — rejected: it was the source of the divergence above. Letting each store mint and return a key of its own choosing meant three shapes that disagreed, a Postgres pre-parse that interpreted a value the contract called opaque, and a `KeyMalformed` failure mode that only one store could ever produce. A 1.0.0 freeze would have canonised all of it onto frozen public/wire surfaces, making it unbounded-cost to fix later.

- **A formalized non-`EventId` key format** (write the contract down, but as an invented shape) — rejected: it would constrain every future substrate to negotiate or honour a key format Edict invented, rather than reusing an identity the system already has. Keying on the `EventId` writes the same contract against the existing stable primary identity, so a future substrate (DynamoDB single-table, Cosmos partition-key) inherits "keyed by `EventId`" for free and encodes one well-known `Guid` as its backend key with nothing to invent.

- **Server-assigned / content-addressed keys returned by the store** — rejected: no realistic claim-check backend needs to choose its own key, and keeping the *return a key* capability is exactly what forced the pointer field that this decision removes. The capability is given up deliberately.

## Consequences

- ADR-0020 is superseded in part: its "store the blob key, not the fetched body" forensic special-case and its description of the wire hop carrying a minted pointer string no longer hold — the key is the `EventId`, carried on the envelope, and the dead-letter row records `SourceEventId` rather than a separate `ClaimCheckKey`. The rest of ADR-0020 (the universal envelope, the append-only store, the at-commit-boundary measure-and-wrap, the missing-blob → dead-letter funnel) stands unchanged.

- Store writes are loud collision-detectors, not idempotent. `PutAsync` runs exactly once per event at the Outbox enqueue boundary with a freshly-minted unique `EventId` and is never re-called on re-drain (a re-drain re-publishes the persisted pointer), so there is no same-key re-PUT to be idempotent about. Postgres keeps its bare `INSERT` and Azure keeps `overwrite: false`, so a duplicate-key write — a breach of the assign-once `EventId`-uniqueness invariant — throws loudly rather than silently overwriting or serving a stale body.

- The `edict.claim_check.key` span tag keeps its name; its value is now the `EventId` string, so existing observability and alerts are unaffected.

- The wire-shape Verify snapshots for `EdictEventEnvelope`, `EdictDeadLetterEntry`, and `EdictDeadLetterRaised`, and `PublicSurfaceAllowListTests`, were regenerated for the collapsed shape; a cross-substrate conformance battery proves the observable key contract (round-trip by `EventId`, and missing `EventId` → `Substrate`/`BlobMissing`) across Azure, Postgres, Kafka×blob, and in-memory.

This ADR records the decision and rationale. ADR-0020 owns the Claim Check mechanism it amends, ADR-0051 owns the assign-once `EventId`, and ADR-0018 owns the dead-letter forensic surface.
