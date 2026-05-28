# Spike 0001 — Kafka stream provider

**Status:** complete — recommendation GO custom; harness retired.
**Parent PRD:** #133.
**Spike issue:** #134.
**Production landing:** Edict.Kafka tracer bullet #142, hardening #143 — both closed.
**Author / driver:** maintainer (this was HITL — the recommendation at the bottom is a human decision, not the spike's).

> The throwaway harness under `spike/Edict.Spike.Kafka/` was deleted after the GO recommendation was acted on. ADR-0028 is the production-shape decision record; this document is preserved as the evidence the recommendation was built on. File-path citations to spike test classes have been removed; the descriptive evidence (probe snapshots, ordinals, verdicts) is intact.

## Scope

Validate a **custom Orleans `IQueueAdapter` prototype** (authored by Edict, backed by `Confluent.Kafka`) against a Kafka container, on the five acceptance criteria from #134 plus a pre-criterion that decides whether Option A is even structurally viable.

The original spike scope ("validate `OrleansContrib/Orleans.Streams.Kafka`") was closed off pre-spike — the community package has no .NET 10 / Orleans 10 build path and forking it is off the table (see #134 status note, 2026-05-26). The remaining recommendation is **GO custom** vs **PIVOT to Azure Event Hubs**.

## Spike harness layout (since deleted)

- Lived under `spike/Edict.Spike.Kafka/` (throwaway; hard wall — explicitly not to be copy-pasted into `Edict.Kafka`). Removed once #142/#143 landed.
- Five projects: `Contracts`, `Adapter` (the custom `IQueueAdapter`), `Silo`, `AppHost` (Aspire + `confluent-local`), `Tests`.
- Criterion battery ran against **Testcontainers-Kafka + `Orleans.TestingHost`** (silo logs in xUnit output, deterministic shutdown, fast iteration). The Aspire `AppHost` was a manual-boot smoke harness; criterion tests did not depend on it. Five tests, 27 s wall-clock end-to-end.
- Wire format: **MessagePack direct** via `MessagePackSerializer` with `TypelessContractlessStandardResolver` for the polymorphic event payload. No Orleans `Serializer` hop in the adapter; envelope was a keyed `[MessagePackObject]` carrying `byte[][]` event payloads.
- Custom adapter floors (echoed forward into ADR-0028):
  - Producer: `Acks.All`, `EnableIdempotence=true`, `CompressionType.Lz4`.
  - Consumer: `EnableAutoCommit=false`, `AutoOffsetReset.Latest` in prod (tests used `Earliest` to dodge a startup-race that only shows up under deterministic immediate-publish — production warms up before publishing). Manual commit deferred to `MessagesDeliveredAsync`.
  - One `IQueueAdapterReceiver` per Kafka partition. Stream → partition via stable FNV hash of stream key.
  - Topic provisioner as an `IHostedService` created the topic with the configured partition count before receivers initialised — broker auto-create defaults to one partition, which would silently break the multi-partition mapping.

## Pre-criterion — `MessagesDeliveredAsync` timing

> Orleans' `IQueueAdapterReceiver.MessagesDeliveredAsync` must fire **after** the consumer grain's `HandleAsync` returns, not before. If Orleans' pulling agent / queue cache pre-acks (calls the hook before dispatch), Option A is structurally unable to honour the ADR-0002 at-least-once contract on Kafka — the broker offset advances while the handler is in flight, and a silo crash mid-handler loses the event.

| Verdict | Observation | Evidence |
| --- | --- | --- |
| **GO** | `MessagesDeliveredAsync` for offset 0 fired at ordinal 23 (stamp `16:49:51.991`) — strictly after `HandleAsyncExit` for the same event at ordinal 20 (stamp `16:49:51.957`), a ~34 ms gap. Orleans' pulling agent commits in its own loop *after* dispatching the cached batch to consumers and observing handler completion. The Kafka offset is committed only after `HandleAsyncExit` returns, so a silo crash mid-handler leaves the offset un-committed and Kafka redelivers on restart — exactly the at-least-once shape ADR-0002 requires. | probe snapshot captured by a `(timestamp, ordinal, kind)` log read via `ISpikeProbeGrain.SnapshotAsync()` |

How we measured it: the probe log recorded `(timestamp, ordinal, kind)` triples for `MessagesDeliveredAsync`, `HandleAsyncEnter`, and `HandleAsyncExit` per event. A test published a single event via a silo-resident publisher grain, waited for a recorder grain to observe it, read the probe log, and asserted `MessagesDeliveredAsync.Ordinal > HandleAsyncExit.Ordinal` for the same event.

Probe snapshot extract (matching the event id `0e72bf51-2e6b-4c57-a9d4-eed1131cf9a9`):

```
19  16:49:51.955  HandleAsyncEnter         key=42165ee2-216a-4a74-b127-832ecb934779  eid=0e72bf51-2e6b-4c57-a9d4-eed1131cf9a9
20  16:49:51.957  HandleAsyncExit          key=42165ee2-216a-4a74-b127-832ecb934779  eid=0e72bf51-2e6b-4c57-a9d4-eed1131cf9a9
23  16:49:51.991  MessagesDeliveredAsync   key=42165ee2216a4a74b127832ecb934779       part=3 off=0
```

## Criterion 1 — round-trip a single event

| Verdict | Observation | Evidence |
| --- | --- | --- |
| **PASS** | A single `OrderPlaced` published through the spike provider was produced to Kafka by the custom `IQueueAdapter`, pulled by the partition-3 `IQueueAdapterReceiver`, dispatched to the implicit-subscription handler grain, and observed by the recorder grain. Round-trip wall-clock from `QueueMessageBatchEnter` to `HandleAsyncExit` ≈ tens of milliseconds. | Same probe as the pre-criterion — the event making it from publish to recorder is the round-trip evidence. |

## Criterion 2 — implicit subscription resolves without `PubSubStore`

| Verdict | Observation | Evidence |
| --- | --- | --- |
| **PASS** | A silo configured with **no `PubSubStore` grain storage** *and* `StreamPubSubType.ImplicitOnly` round-trips events end-to-end. Implicit subscriptions are scanned from assembly metadata at silo startup, dispatched directly from the pulling agent to the matching grain activation; no pub-sub channel is involved. PubSubStore is required only for explicit subscriptions, which Edict does not use. | Silo configured without `AddMemoryGrainStorage("PubSubStore")`; provider opted in via `pubSubType: StreamPubSubType.ImplicitOnly`. |

Implication for `Edict.Kafka`: the provider does not need to require consumers to wire a PubSubStore. ADR-0028 should call out `StreamPubSubType.ImplicitOnly` as the default for the provider.

## Criterion 3 — per-aggregate ordering under parallel publishes

| Verdict | Observation | Evidence |
| --- | --- | --- |
| **PASS** | 8 aggregates × 10 events each = 80 events published in parallel (one publisher grain per aggregate, all `Task.WhenAll`'d). For every aggregate, the observed `Sequence` numbers on the recorder are strictly increasing (`[0,1,2,3,4,5,6,7,8,9]`). Per-aggregate ordering is preserved by the partition-key → partition mapping plus per-partition ordering Kafka guarantees; cross-aggregate ordering is intentionally undefined and was scrambled, as expected. | Recorder grain snapshot, 80 events / 8 aggregates. |

## Criterion 4 — mid-handler crash → exactly-once eventual effect

| Verdict | Observation | Evidence |
| --- | --- | --- |
| **PASS** | Cluster A's handler grain is armed to hang on a specific `EventId`. After publish, the test waits for the fault-injection latch to fire (handler entered, await pending), then calls `TestCluster.KillSiloAsync` to forcefully terminate the silo. `MessagesDeliveredAsync` never fires for that offset, so the Kafka consumer group offset stays at its prior value. Cluster B re-deploys with the *same* consumer group + same topic. The Kafka consumer resumes from the un-committed offset; the same `EventId` is redelivered to cluster B's freshly-activated handler grain; the recorder grain observes the event. Eventual exactly-once effect is delivered to the recorder; ADR-0002's dedup ring (not in the spike scope; tested elsewhere) suppresses the duplicate handler invocation in production. | Two-cluster TestingHost run with shared consumer group + topic. |

Note: TestingHost in-process `KillSiloAsync` was forceful for spike purposes. The separate-process crash shape now lives in `Edict.Kafka.Tests/Resilience/` per ADR-0024 conformance harness shape.

## Criterion 5 — mid-batch crash → second event redelivered

| Verdict | Observation | Evidence |
| --- | --- | --- |
| **PASS** | Cluster A publishes 2 events to the same aggregate (same Kafka partition) in a single batch via a publisher grain's `PublishManyAsync`. The handler hangs on event 2's `EventId`. `KillSiloAsync` interrupts. The Kafka consumer offset advanced past event 1 only if `MessagesDeliveredAsync` was called for that batch; with the hang on event 2, the batch never completed, so offset stays at its prior value. Cluster B re-deploys with the same consumer group; the test asserts event 2 is observed by the recorder grain after restart. (Event 1 may also be redelivered depending on Orleans' batch-ack granularity — that's the at-least-once property; production dedup handles it.) | Two-cluster TestingHost run, batched publish, hang on second event. |

## Summary verdicts

| Item | Verdict |
| --- | --- |
| Pre-criterion (`MessagesDeliveredAsync` timing) | GO |
| Criterion 1 (round-trip) | PASS |
| Criterion 2 (no PubSubStore) | PASS |
| Criterion 3 (per-aggregate ordering) | PASS |
| Criterion 4 (mid-handler crash) | PASS |
| Criterion 5 (mid-batch crash) | PASS |

## Recommendation (taken)

**GO custom.** All five criteria passed, pre-criterion held, no counter-evidence surfaced. Production landing tracked at:

- **#142** — `Edict.Kafka` tracer bullet (custom `IQueueAdapter`, four happy-path conformance, eight adapter contract assertions, ADR-0028 draft). Closed.
- **#143** — `Edict.Kafka` hardening (full options surface, contract floors, per-stream topology, Kafka×Postgres + Kafka×Azure conformance, resilience suite, throughput sweep, ADR-0028 Accepted). Closed slices 1-8.

Caveats logged at the time the recommendation was written (now mostly addressed by #142/#143):

- All criteria were proven on `confluentinc/cp-kafka` (Testcontainers default), not `confluent-local`. The Aspire smoke confirmed `confluent-local` boots and accepts the same Confluent.Kafka client config, but the criteria themselves were not re-run against it.
- Crash tests used in-process `TestCluster.KillSiloAsync`. Real OS-level kill (separate silo process) now lives in `Edict.Kafka.Tests/Resilience/` per ADR-0024.
- Performance / throughput was not measured here. PRD #133's trigger 2 (performance) was carried into the #143 sweep.
- The spike's wire format (MessagePack direct, `TypelessContractlessStandardResolver`) embedded .NET type names; production `Edict.Kafka` uses the existing `EdictContractSerializer` MessagePack plug-in (ADR-0006/0007 wire-stability discipline) — the spike's format choice did not pre-commit `Edict.Kafka`'s.
