# Resolved — list-projection over-count was a harness artifact, not a production bug

**Status:** RESOLVED 2026-06-06. See **ADR-0062** for the full decision and the corrected **ADR-0002** amendment.

## What it turned out to be

The recurring `DrainCoversEveryDispatchTests` CI flake (`placed=2` / `increment=5`, and under heavier overlap `increment=1`) was **not** a production-reachable framework defect. It was manufactured by the in-memory Test Framework delivering a single aggregate's events to one projection activation **concurrently**, which real Orleans never does.

The earlier diagnosis in this doc (and in the `list-projection-cumulative-snapshot-overcount` memory) asserted the opposite — "real, production-reachable correctness defect." That was **wrong**, and is corrected.

## How it was proven and fixed

- **Proof of innocence:** real Azure Queue Streams *and* real Kafka deliver a burst of events to one projection activation strictly serially (handler-turn high-water mark of one, exact count). A real pulling agent awaits each `OnNextAsync` before the next, and `OnNextAsync` is non-reentrant. The harness, by contrast, fired concurrent fire-and-forget deliveries into the `[AlwaysInterleave]` `OnEdictEventAsync` seam — recorded at five concurrent turns on one activation, with the row corrupted.
- **Fix (all in `Edict.Testing` / `Edict.Core`):** `InProcPublishExecutor` now serializes deliveries per `(grain class, route key)` activation; the `[AlwaysInterleave]` attribute on `IEdictEventConsumer.OnEdictEventAsync` is removed (both delivery seams now share one non-reentrant contract); the `InconsistentStateException` retry loop is removed (it only papered over the now-eliminated write races).
- **Guards:** `EventConsumerReentrancyParityTests` (structural), `DeliverySerializationTests` (harness behavioural), `ProjectionDeliverySerializationScenarios` (streaming conformance, Azure + Kafka, binding-enforced).

## Preserved evidence (can be cleaned up)

The 2-core Docker repro worktree at `C:\Projects\edict-flakerepro` and its captured trace are no longer needed for diagnosis — the fix is verified deterministically on the dev box via `DeliverySerializationTests`. Safe to delete once the change has landed.
