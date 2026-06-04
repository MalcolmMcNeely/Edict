# Persistence-axis resilience (Postgres)

Real store-disruption recovery for the Postgres persistence binding. Folded here
from `Edict.Postgres.Tests` under the conformance axis-split (ADR-0054, #295):
store faults are persistence properties, so they live with the persistence
battery, over a **dumb deliver-once `MemoryStreams` reference** — only the store
is faulted.

## Why this collection owns its own Postgres container

The persistence battery shares one Postgres through `PostgresAssemblyHost` with a
per-fixture database. Faulting that container would break every parallel battery
collection, so the resilience fixture owns its own container and the collection is
`DisableParallelization = true`. It also wires `ControllableOutboxExecutor` so the
drain-recovery scenario can stage a durable pending entry.

## Why `docker stop`, not `docker pause`

The streaming suites pause (queue-redelivery shape). Store faults must stop: a
pause freezes an in-flight write inside the backend and replays it on resume, so a
write the client already saw time out still commits — an ambiguous outcome that
would mask the dirty-activation-drop the write-fault scenario asserts. Stopping
rolls the uncommitted statement back, the genuine connection-drop shape. The host
port is pinned with a fixed binding so stop/start reuses it and Edict reconnects
through its existing data source.

## Scenarios

1. `PostgresStoppedMidDrainTests` — a one-shot synthetic publish failure stages a
   durable pending outbox entry and arms the drain reminder. Postgres is stopped
   and a reminder tick driven; the publish reaches the stream but the ack
   write-back faults and rolls back, so the entry stays pending. After restart, a
   reminder tick reconnects and drains to empty. Because EventId is assigned once
   as the event enters the outbox and carried on the persisted payload (#277), the
   recovery re-publish carries the same id and the consumer dedup ring collapses
   it across the two deliveries the deliver-once reference stream made — pinning
   effectively-once across the ack-write-window fault, not just at-least-once.
2. `PostgresRealWriteFaultTests` — an already-active grain commits one increment
   while Postgres is healthy, then a second command's commit write lands on the
   stopped backend. `EdictPostgresGrainStorage` surfaces a real `NpgsqlException`
   (rethrown as `EdictPostgresStorageException`) mid-`WriteStateAsync`, the
   uncommitted statement rolls back, the dirty activation is dropped, and a retry
   against clean durable state applies exactly once.

## Why no silo-kill scenario

The old cross suite had a `PostgresSiloKilledMidHandler` over AQS streams ×
Postgres storage. Under the axis-split it was redundant: kill-driven redelivery is
a streaming property, proven on a real broker with reference persistence by the
AQS streaming binding and the Kafka binding. The persistence axis runs a dumb
deliver-once stream that never redelivers, so a silo-kill redelivery proof has no
home here.
