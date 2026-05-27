# Kafka resilience tests

Substrate-disruption scenarios for `Edict.Kafka`, slice 7 of issue #143. The
category proves the contract floors (`acks=all`, `enable.idempotence=true`,
`enable.auto.commit=false`) hold under three failure modes: a transient
broker outage, a silo crash mid-`Handle`, and a silo crash mid-batch.

## Two fixtures, two failure sites

`KafkaResilienceClusterFixture` owns its own Kafka container so the suite
can `docker pause` the broker without freezing every other collection's
producer/consumer calls. The Postgres half rides the assembly-shared
`PostgresAssemblyHost` because persistence is the dependency here, not the
failure point — the resilience suite owns only the substrate it disrupts.

`KafkaSiloKillClusterFixture` is the silo-crash side and also owns its own
Kafka container, because `KillSiloAsync` followed by `RestartSiloAsync` leaves
brief windows where the consumer-group state is unstable; sharing the
broker with conformance tests under those conditions would race. Pins the
silo-kill streams to `PartitionCountByStream[...] = 1` so the consistent-ring
queue balancer has exactly one `QueueId` to assign — restart is a
deterministic re-`Assign()` on the same partition, with no rebalance ambiguity
to confuse a single-silo cluster.

## Why `docker pause` and not `docker stop`

Testcontainers .NET 3.10 has no `PauseAsync` on `IContainer`, so both fixtures
talk to Docker.DotNet directly. `docker pause` was chosen over `docker stop`
\+ `docker start` because the latter re-binds the container to a new
ephemeral host port. The silo's already-configured `Confluent.Kafka` producer
and consumer hold the old port and would never reconnect, masking what we
want to prove (Edict's reconnect-and-finish-publish behaviour) behind a
host-wiring artefact. `docker pause` preserves the port and the
SIGSTOP-suspended Kafka process resumes from exactly where it was on
unpause — the closest analogue to a transient broker disruption.

## Scenarios

1. `KafkaPausedMidPublishTests` — events 1-3 publish under normal broker
   conditions, the broker is paused, events 4-6 publish sequentially while
   librdkafka blocks on `acks=all`, the broker resumes, the pending publishes
   complete and the consumer drains all six in `[1..6]` order.
2. `KafkaSiloKilledMidHandlerTests` — single event, slow `Handle`, kill the
   silo before `MessagesDeliveredAsync` runs. After restart the new consumer
   reads from the pre-event committed offset, redelivers, and the
   `EdictTableProjectionBuilder`'s atomic ring + `UpsertRow` write settles
   the row at `Count = 1` despite two `Handle` entries.
3. `KafkaSiloKilledMidBatchTests` — two events published back-to-back, both
   reach the partition before the kill. The slow first `Handle` blocks the
   pulling agent's `MessagesDeliveredAsync`, so neither offset commits. After
   restart, both events redeliver and the row settles at `Count = 2`.

## Multi-broker rebalance — explicitly deferred

The original issue text framed scenario 1 as "broker kill + consumer-group
rebalance under load". Multi-broker rebalance correctness on top of
Edict.Kafka's `Assign`-based receiver is a #139b multi-silo concern — the
custom adapter does not yet wire Orleans' queue balancer to Kafka's
consumer-group rebalance protocol. The slice 7 deliverable pins the
contract-floor invariant (no events lost or reordered through a transient
broker outage) on the single-broker single-silo topology the adapter
currently supports; the rebalance-distribution claim follows when #139b
lands.

## Serial within the assembly

`xunit.runner.json` already pins `parallelizeTestCollections = false` from
slice 6. The resilience collections add `DisableParallelization = true` on
their `CollectionDefinition` for the same reason the Azure suite does — each
collection owns Docker.DotNet control over its own Kafka container, and a
mis-timed pause or kill from another collection would corrupt its substrate.
