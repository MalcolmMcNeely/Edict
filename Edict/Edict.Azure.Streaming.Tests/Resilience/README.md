# Streaming-axis resilience (AQS)

Transport-fault and silo-kill scenarios for the AQS streaming binding. Folded
here from `Edict.Azure.Tests` under the conformance axis-split (ADR-0054, #295):
broker-disruption and kill-driven redelivery are streaming properties, so they
live with the streaming battery.

## Reference persistence — the broker is the only fault point

Each fixture stands up a **real** Azure Queue stream over a fixture-owned Azurite
and **reference persistence** (Orleans memory grain storage + the in-memory
reference table / claim-check stores). The store is never the fault and is never
asserted on — except the one projection row the silo-kill proof reads back from
the reference table store after redelivery, which is a streaming property (the
row settles once under redelivery), not a real-store assertion. Because the store
is in-memory, a paused Azurite cannot perturb it: the fault is isolated to the
streaming axis.

## Why this collection owns its own Azurite container

The streaming battery shares one Azurite through `AzuriteAssemblyHost` with
per-fixture resource names. That breaks the moment a test pauses Azurite — every
other collection's queue call would hang until unpause. So the resilience
collections opt out: each fixture starts and disposes its own `AzuriteContainer`,
and both collections are `DisableParallelization = true`.

## Why `docker pause`, not stop+start

Pause preserves the host port binding, so the silo's already-configured Azure
Queue client reconnects and the test exercises Edict's retry-and-converge
behaviour. Stop+start re-binds to a new ephemeral port, masking that behaviour
behind a host-wiring artefact.

## Scenarios

1. `AzuriteStoppedMidPublishTests` — event published, Azurite paused, unpaused.
   Asserts exactly-once delivery.
2. `AzuriteRestartedMidSagaTests` — saga trigger published, Azurite paused past
   the queue visibility timeout, unpaused. Asserts the saga records progress once
   and the tracker command lands once.
3. `AzuriteUnavailableAtStartupTests` — Azurite paused before the first
   substrate-touching call. Asserts the publish hangs while paused and completes
   exactly-once after unpause.
4. `SiloKilledMidHandlerTests` — a slow projection blocks; `KillSiloAsync` tears
   the activation down before the atomic ring + `UpsertRow` commit, so the first
   turn writes nothing. The AQS message returns to visible and a surviving silo
   redelivers, settling the reference-store row at `Count = 1` despite two
   `Handle` entries.
