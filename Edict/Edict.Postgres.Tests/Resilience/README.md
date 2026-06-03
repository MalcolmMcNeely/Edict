# Postgres resilience tests

Real-fault recovery against the Postgres persistence substrate, issue #270.
Resilience/fault-injection is per-provider; Azure and Kafka each have a
resilience suite, Postgres did not. The conformance battery proves the *core*
recovery logic on real Postgres, but it injects a synthetic in-process
exception. This suite closes the residual gap: Edict's recovery against *real*
Postgres faults.

## Two fixtures, two failure sites

`PostgresResilienceClusterFixture` owns its own Postgres container so the suite
can take the backend down without breaking every parallel conformance
collection running against the assembly-shared `PostgresAssemblyHost`. Azurite
(the AQS streams half) rides the shared assembly host because streaming is the
dependency here, not the failure point — the suite owns only the substrate it
disrupts. It also wires the `ControllableOutboxExecutor` so the drain-recovery
scenario can stage a durable pending entry; the fault under test is the real
Postgres outage on the recovery drain's state write-back.

`PostgresSiloKillClusterFixture` is the silo-crash side. Postgres is not the
failure point there (the silo is), so it reuses the shared `PostgresAssemblyHost`
with its own per-fixture database — non-corrupting and avoiding a third
container.

## Why `docker stop` and not `docker pause`

The Azure and Kafka suites pause their containers because their faults are
queue-redelivery shapes, where a SIGSTOP-suspended broker resuming from exactly
where it left off is the right analogue. This suite does the opposite, and the
reason is the write path.

A Docker pause freezes an in-flight write *inside* the backend and replays it on
resume: a write the client already saw time out still commits server-side. That
ambiguous outcome would mask the dirty-activation-drop the write-fault scenario
asserts — the faulted turn's mutation would silently persist, and a retry would
double-apply. Stopping the backend rolls the uncommitted statement back, which
is the genuine connection-drop shape. The host port is pinned with a fixed
binding so stop/start reuses it and Edict reconnects through its existing data
source, rather than seeing a host-port rebind.

## Scenarios

1. `PostgresStoppedMidDrainTests` — a one-shot synthetic publish failure stages
   a durable pending outbox entry and arms the drain reminder. Postgres is then
   stopped and a reminder tick driven; the publish reaches the stream but the
   ack write-back faults and rolls back, so the entry stays pending. After
   restart, a reminder tick reconnects and drains the outbox to empty,
   unregistering the reminder. This pins the producer-side recovery guarantee.
2. `PostgresRealWriteFaultTests` — an already-active grain commits one increment
   while Postgres is healthy, then a second command's commit write lands on the
   stopped backend. `EdictPostgresGrainStorage` surfaces a real `NpgsqlException`
   (rethrown as `EdictPostgresStorageException`) mid-`WriteStateAsync`, the
   uncommitted statement rolls back, the dirty activation is dropped, and a
   retry against clean durable state applies exactly once.
3. `PostgresSiloKilledMidHandlerTests` — parity with the Azure and Kafka
   silo-kill tests, on the AQS-streams × Postgres-grain-storage cross. A slow
   projection blocks; `KillSiloAsync` tears the activation down before the
   atomic ring + `UpsertRow` commit. The AQS message redelivers and the Postgres
   row settles at `Count = 1` despite two `Handle` entries.

## Drain recovery is producer-side, not end-to-end exactly-once

Scenario 1 deliberately does not assert end-to-end exactly-once. In `DrainAsync`
the publish precedes the Postgres ack-write, so any real Postgres drain fault
lands *post-publish* and the recovery re-drain re-publishes. `PublishEventExecutor`
stamps a fresh `EventId` per publish, so that re-publish is a distinct event the
consumer dedup ring cannot collapse — at-least-once, not exactly-once. The
synthetic `OutboxRecoveryAfterCrash` conformance test avoids this only because
it fails the *publish itself*, so nothing is shipped on the failed pass. That
gap is tracked as a separate framework finding.

## Classifier fix

A Postgres substrate fault once routed to the catch-all `Unhandled` bucket
instead of `Substrate`, because `Edict.Core` cannot reference Npgsql to
recognise the type. Fault classification is now a registered extension point:
`Edict.Core` classifies framework causes first in a privileged switch, and only
on fallthrough consults the `IDeadLetterFaultClassifier` instances each provider
registers. `Edict.Postgres` ships `PostgresDeadLetterFaultClassifier`, which
matches `NpgsqlException` (the base that also covers `PostgresException`) and
the rethrown `EdictPostgresStorageException` wrapper and maps both to
`Substrate`; it is registered in `AddEdictPostgresPersistence`. The match is by
real exception type, not a type-name string, so it is compile-checked and
survives a driver rename. Pinned in `PostgresDeadLetterFaultClassifierTests` and
in the Core composition tests (`DeadLetterFailureClassifierTests`).

## Serial collections

Both collections set `DisableParallelization = true`: each owns control over its
own container or drives a silo kill, and a mis-timed stop or kill from another
collection would corrupt its substrate.
