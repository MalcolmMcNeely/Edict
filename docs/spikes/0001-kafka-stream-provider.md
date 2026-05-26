# Spike 0001 — Kafka stream provider

**Status:** in progress (no run data yet).
**Parent PRD:** #133.
**Spike issue:** #134.
**Author / driver:** maintainer (this is HITL — the recommendation at the bottom is a human decision, not the spike's).

## Scope

Validate a **custom Orleans `IQueueAdapter` prototype** (authored by Edict, backed by `Confluent.Kafka`) against an Aspire `confluent-local` Kafka container, on the five acceptance criteria from #134 plus a pre-criterion that decides whether Option A is even structurally viable.

The original spike scope ("validate `OrleansContrib/Orleans.Streams.Kafka`") was closed off pre-spike — the community package has no .NET 10 / Orleans 10 build path and forking it is off the table (see #134 status note, 2026-05-26). The recommendation is now **GO custom** vs **PIVOT to Azure Event Hubs**.

## Spike harness location

- `spike/Edict.Spike.Kafka/` (throwaway; hard wall — must not be copy-pasted into a future `Edict.Kafka` package).
- Five projects: `Contracts`, `Adapter` (the custom `IQueueAdapter`), `Silo`, `AppHost` (Aspire + `confluent-local`), `Tests`.
- Wire format: **MessagePack direct** via `MessagePackSerializer` with `TypelessContractlessStandardResolver` for the polymorphic event payload. No Orleans `Serializer` hop in the adapter; envelope is a keyed `[MessagePackObject]` carrying `byte[][]` event payloads. This matches what a production `Edict.Kafka` provider would do.
- Custom adapter floors (echoes ADR-0028 intent):
  - Producer: `Acks.All`, `EnableIdempotence=true`, `CompressionType.Lz4`.
  - Consumer: `EnableAutoCommit=false`, `AutoOffsetReset.Latest`. Manual commit deferred to `MessagesDeliveredAsync`.
  - One `IQueueAdapterReceiver` per Kafka partition. Stream → partition via stable FNV hash of stream key.

## Pre-criterion — `MessagesDeliveredAsync` timing

> Orleans' `IQueueAdapterReceiver.MessagesDeliveredAsync` must fire **after** the consumer grain's `HandleAsync` returns, not before. If Orleans' pulling agent / queue cache pre-acks (calls the hook before dispatch), Option A is structurally unable to honour the ADR-0002 at-least-once contract on Kafka — the broker offset advances while the handler is in flight, and a silo crash mid-handler loses the event.

| Verdict | Observation | Evidence |
| --- | --- | --- |
| **GO** | `MessagesDeliveredAsync` for offset 0 fired at ordinal 23 (stamp `16:49:51.991`) — strictly after `HandleAsyncExit` for the same event at ordinal 20 (stamp `16:49:51.957`), a ~34 ms gap. Orleans' pulling agent commits in its own loop *after* dispatching the cached batch to consumers and observing handler completion. The Kafka offset is committed only after `HandleAsyncExit` returns, so a silo crash mid-handler leaves the offset un-committed and Kafka redelivers on restart — exactly the at-least-once shape ADR-0002 requires. | `spike/Edict.Spike.Kafka/Edict.Spike.Kafka.Tests/PreCriterionTests.cs::MessagesDeliveredAsync_fires_after_HandleAsync_returns`; probe snapshot captured by `SpikePreCriterionLog` and read via `ISpikeProbeGrain.SnapshotAsync()` |

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
| TBD | — | — |

(The silo currently registers an in-memory `PubSubStore` for safety. The actual Criterion-2 test removes that registration and asserts implicit subscriptions still dispatch.)

## Criterion 3 — per-aggregate ordering under parallel publishes

| Verdict | Observation | Evidence |
| --- | --- | --- |
| TBD | — | — |

## Criterion 4 — mid-handler crash → exactly-once eventual effect

| Verdict | Observation | Evidence |
| --- | --- | --- |
| TBD | — | — |

## Criterion 5 — mid-batch crash → second event redelivered

| Verdict | Observation | Evidence |
| --- | --- | --- |
| TBD | — | — |

## Reproducible setup

```pwsh
# 1. Boot Kafka (confluent-local) + silo together via Aspire.
dotnet run --project spike/Edict.Spike.Kafka/Edict.Spike.Kafka.AppHost

# 2. In another shell, run the criterion battery.
dotnet test spike/Edict.Spike.Kafka/Edict.Spike.Kafka.Tests
```

Aspire dashboard (HTTP) defaults to `http://localhost:15090`; Kafka UI is wired via `.WithKafkaUI()` and is reachable from the dashboard's resource list.

## Recommendation

**TBD — pending pre-criterion observation.**

- **GO custom** — all five criteria pass AND pre-criterion holds. Proceed with `Edict.Kafka` (issues #139a tracer bullet + #139b hardening).
- **PIVOT to Azure Event Hubs** — any criterion fails OR pre-criterion fails. `#139a + #139b` collapse to a single `#139-EH — Edict.EventHubs provider` issue. ADR-0028 reshapes; bail-out triggers 1+3 retire, trigger 2 (performance) carries over to EH.

This recommendation is HITL — the maintainer writes the final verdict after reading the evidence above.
