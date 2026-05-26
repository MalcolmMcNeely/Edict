# Spike 0001 — Kafka stream provider

**Status:** evidence captured; recommendation pending (HITL).
**Parent PRD:** #133.
**Spike issue:** #134.
**Author / driver:** maintainer (this is HITL — the recommendation at the bottom is a human decision, not the spike's).

## Scope

Validate a **custom Orleans `IQueueAdapter` prototype** (authored by Edict, backed by `Confluent.Kafka`) against a Kafka container, on the five acceptance criteria from #134 plus a pre-criterion that decides whether Option A is even structurally viable.

The original spike scope ("validate `OrleansContrib/Orleans.Streams.Kafka`") was closed off pre-spike — the community package has no .NET 10 / Orleans 10 build path and forking it is off the table (see #134 status note, 2026-05-26). The remaining recommendation is **GO custom** vs **PIVOT to Azure Event Hubs**.

## Spike harness location

- `spike/Edict.Spike.Kafka/` (throwaway; hard wall — must not be copy-pasted into a future `Edict.Kafka` package).
- Five projects: `Contracts`, `Adapter` (the custom `IQueueAdapter`), `Silo`, `AppHost` (Aspire + `confluent-local`), `Tests`.
- Criterion battery runs against **Testcontainers-Kafka + `Orleans.TestingHost`** (silo logs land in xUnit output, deterministic shutdown, fast iteration). The Aspire `AppHost` is kept as a manual-boot smoke harness; criterion tests do not depend on it. Five tests, 27 s wall-clock end-to-end.
- Wire format: **MessagePack direct** via `MessagePackSerializer` with `TypelessContractlessStandardResolver` for the polymorphic event payload. No Orleans `Serializer` hop in the adapter; envelope is a keyed `[MessagePackObject]` carrying `byte[][]` event payloads. This matches what a production `Edict.Kafka` provider would do.
- Custom adapter floors (echoes ADR-0028 intent):
  - Producer: `Acks.All`, `EnableIdempotence=true`, `CompressionType.Lz4`.
  - Consumer: `EnableAutoCommit=false`, `AutoOffsetReset.Latest` in prod (tests use `Earliest` to dodge a startup-race that only shows up under deterministic immediate-publish — production would warm up before publishing). Manual commit deferred to `MessagesDeliveredAsync`.
  - One `IQueueAdapterReceiver` per Kafka partition. Stream → partition via stable FNV hash of stream key.
  - Topic provisioner (`SpikeKafkaTopicProvisioner`, an `IHostedService`) creates the topic with the configured partition count before receivers initialise — broker auto-create defaults to one partition, which would silently break the multi-partition mapping.

## Pre-criterion — `MessagesDeliveredAsync` timing

> Orleans' `IQueueAdapterReceiver.MessagesDeliveredAsync` must fire **after** the consumer grain's `HandleAsync` returns, not before. If Orleans' pulling agent / queue cache pre-acks (calls the hook before dispatch), Option A is structurally unable to honour the ADR-0002 at-least-once contract on Kafka — the broker offset advances while the handler is in flight, and a silo crash mid-handler loses the event.

| Verdict | Observation | Evidence |
| --- | --- | --- |
| **GO** | `MessagesDeliveredAsync` for offset 0 fired at ordinal 23 (stamp `16:49:51.991`) — strictly after `HandleAsyncExit` for the same event at ordinal 20 (stamp `16:49:51.957`), a ~34 ms gap. Orleans' pulling agent commits in its own loop *after* dispatching the cached batch to consumers and observing handler completion. The Kafka offset is committed only after `HandleAsyncExit` returns, so a silo crash mid-handler leaves the offset un-committed and Kafka redelivers on restart — exactly the at-least-once shape ADR-0002 requires. | `Edict.Spike.Kafka.Tests/PreCriterionTests.cs::MessagesDeliveredAsync_fires_after_HandleAsync_returns`; probe snapshot captured by `SpikePreCriterionLog` and read via `ISpikeProbeGrain.SnapshotAsync()` |

How we measured it: `SpikePreCriterionLog` records `(timestamp, ordinal, kind)` triples for `MessagesDeliveredAsync`, `HandleAsyncEnter`, and `HandleAsyncExit` per event. The test publishes a single event via `IPublisherGrain` (silo-resident), waits for the recorder grain to observe it, reads the probe log, and asserts `MessagesDeliveredAsync.Ordinal > HandleAsyncExit.Ordinal` for the same event.

Probe snapshot extract (matching the event id `0e72bf51-2e6b-4c57-a9d4-eed1131cf9a9`):

```
19  16:49:51.955  HandleAsyncEnter         key=42165ee2-216a-4a74-b127-832ecb934779  eid=0e72bf51-2e6b-4c57-a9d4-eed1131cf9a9
20  16:49:51.957  HandleAsyncExit          key=42165ee2-216a-4a74-b127-832ecb934779  eid=0e72bf51-2e6b-4c57-a9d4-eed1131cf9a9
23  16:49:51.991  MessagesDeliveredAsync   key=42165ee2216a4a74b127832ecb934779       part=3 off=0
```

## Criterion 1 — round-trip a single event

| Verdict | Observation | Evidence |
| --- | --- | --- |
| **PASS** | A single `OrderPlaced` published through the spike provider was produced to Kafka by the custom `IQueueAdapter`, pulled by the partition-3 `IQueueAdapterReceiver`, dispatched to the implicit-subscription `OrderHandlerGrain`, and observed by the `RecorderGrain`. Round-trip wall-clock from `QueueMessageBatchEnter` to `HandleAsyncExit` ≈ tens of milliseconds. | Same test as the pre-criterion — the event making it from publish to recorder is the round-trip evidence. |

## Criterion 2 — implicit subscription resolves without `PubSubStore`

| Verdict | Observation | Evidence |
| --- | --- | --- |
| **PASS** | A silo configured with **no `PubSubStore` grain storage** *and* `StreamPubSubType.ImplicitOnly` round-trips events end-to-end. Implicit subscriptions are scanned from assembly metadata at silo startup, dispatched directly from the pulling agent to the matching grain activation; no pub-sub channel is involved. PubSubStore is required only for explicit subscriptions, which Edict does not use. | `Edict.Spike.Kafka.Tests/Criterion2Tests.cs::Implicit_subscription_resolves_without_PubSubStore` with `NoPubSubStoreSiloConfigurator` (no `AddMemoryGrainStorage("PubSubStore")` line; provider opts in via `AddSpikeKafkaStreams(..., pubSubType: StreamPubSubType.ImplicitOnly)`) |

Implication for `Edict.Kafka`: the provider does not need to require consumers to wire a PubSubStore. ADR-0028 should call out `StreamPubSubType.ImplicitOnly` as the default for the provider.

## Criterion 3 — per-aggregate ordering under parallel publishes

| Verdict | Observation | Evidence |
| --- | --- | --- |
| **PASS** | 8 aggregates × 10 events each = 80 events published in parallel (one publisher grain per aggregate, all `Task.WhenAll`'d). For every aggregate, the observed `Sequence` numbers on the recorder are strictly increasing (`[0,1,2,3,4,5,6,7,8,9]`). Per-aggregate ordering is preserved by the partition-key → partition mapping plus per-partition ordering Kafka guarantees; cross-aggregate ordering is intentionally undefined and was scrambled, as expected. | `Edict.Spike.Kafka.Tests/Criterion3Tests.cs::PerAggregate_order_is_preserved_under_parallel_publishes` |

## Criterion 4 — mid-handler crash → exactly-once eventual effect

| Verdict | Observation | Evidence |
| --- | --- | --- |
| **PASS** | Cluster A's `OrderHandlerGrain` is armed to hang on a specific `EventId`. After publish, the test waits for `SpikeFaultInjection.WaitEnteredAsync` to fire (handler entered, await pending), then calls `TestCluster.KillSiloAsync` to forcefully terminate the silo. `MessagesDeliveredAsync` never fires for that offset, so the Kafka consumer group offset stays at its prior value. Cluster B re-deploys with the *same* consumer group + same topic. The Kafka consumer resumes from the un-committed offset; the same `EventId` is redelivered to cluster B's freshly-activated `OrderHandlerGrain`; the recorder grain observes the event. Eventual exactly-once effect is delivered to the recorder; ADR-0002's dedup ring (not in the spike scope; tested elsewhere) suppresses the duplicate handler invocation in production. | `Edict.Spike.Kafka.Tests/Criterion4Tests.cs::MidHandler_crash_redelivers_after_restart` |

Note: TestingHost in-process `KillSiloAsync` is forceful for spike purposes. A separate-process crash test (via the AppHost) would be the next step for the production-shaped `Edict.Kafka` provider test suite — that lives in `Edict.Kafka.Tests/Resilience/` per ADR-0024 conformance harness shape.

## Criterion 5 — mid-batch crash → second event redelivered

| Verdict | Observation | Evidence |
| --- | --- | --- |
| **PASS** | Cluster A publishes 2 events to the same aggregate (same Kafka partition) in a single batch via `IPublisherGrain.PublishManyAsync`. The handler hangs on event 2's `EventId`. `KillSiloAsync` interrupts. The Kafka consumer offset advanced past event 1 only if `MessagesDeliveredAsync` was called for that batch; with the hang on event 2, the batch never completed, so offset stays at its prior value. Cluster B re-deploys with the same consumer group; the test asserts event 2 is observed by the recorder grain after restart. (Event 1 may also be redelivered depending on Orleans' batch-ack granularity — that's the at-least-once property; production dedup handles it.) | `Edict.Spike.Kafka.Tests/Criterion5Tests.cs::MidBatch_crash_redelivers_unhandled_events` |

## Reproducible setup

Whole battery (5 tests + pre-criterion) runs in ~27 s on a developer laptop, against a Testcontainers-managed `confluentinc/cp-kafka` container:

```pwsh
dotnet test spike/Edict.Spike.Kafka/Edict.Spike.Kafka.Tests
```

Manual Aspire smoke (kept for future crash-tests + the maintainer's eye-test of `confluent-local` + Kafka UI):

```pwsh
dotnet run --project spike/Edict.Spike.Kafka/Edict.Spike.Kafka.AppHost
```

The Aspire dashboard (HTTP) defaults to `http://localhost:15090`; Kafka UI is wired via `.WithKafkaUI()` and is reachable from the dashboard's resource list.

## Summary verdicts

| Item | Verdict |
| --- | --- |
| Pre-criterion (`MessagesDeliveredAsync` timing) | GO |
| Criterion 1 (round-trip) | PASS |
| Criterion 2 (no PubSubStore) | PASS |
| Criterion 3 (per-aggregate ordering) | PASS |
| Criterion 4 (mid-handler crash) | PASS |
| Criterion 5 (mid-batch crash) | PASS |

## Recommendation

**TBD — HITL (maintainer to fill).**

- **GO custom** — all five criteria pass AND pre-criterion holds. Proceed with `Edict.Kafka` (issues #139a tracer bullet + #139b hardening). All evidence above supports this option; the spike has produced no counter-evidence.
- **PIVOT to Azure Event Hubs** — any criterion fails OR pre-criterion fails. `#139a + #139b` collapse to a single `#139-EH — Edict.EventHubs provider` issue. ADR-0028 reshapes; bail-out triggers 1+3 retire, trigger 2 (performance) carries over to EH.

Caveats for the maintainer to weigh before writing the final verdict:

- All criteria were proven on `confluentinc/cp-kafka` (the Testcontainers default), not `confluent-local`. The Aspire smoke confirmed `confluent-local` boots and accepts the same Confluent.Kafka client config, but the criteria themselves were not re-run against it.
- Crash tests used in-process `TestCluster.KillSiloAsync`. A real OS-level kill (separate silo process) is the higher-confidence shape and belongs in `Edict.Kafka.Tests/Resilience/` once the provider lands.
- Performance / throughput was not measured. PRD #133's trigger 2 (performance) carries over regardless of the GO/PIVOT direction.
- The spike's wire format (MessagePack direct, `TypelessContractlessStandardResolver`) embeds .NET type names. Production `Edict.Kafka` would use the existing `EdictContractSerializer` MessagePack plug-in (ADR-0006/0007 wire-stability discipline) — the spike's format choice does not pre-commit `Edict.Kafka`'s.
- All criteria passing does not prove the absence of structural surprises during the production-shape implementation (Orleans codegen interactions, multi-silo rebalance under load, real-cluster topic-creation auth flows). Those surface during `#139a`/`#139b` and the GO branch should still keep the PIVOT option warm until first integration.

This recommendation is HITL — the maintainer writes the final verdict here.
