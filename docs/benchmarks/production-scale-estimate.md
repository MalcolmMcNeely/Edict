# Edict production-scale estimate

> **Read this first.** This document is a back-of-envelope sizing sketch derived from the laptop benchmarks in [`throughput.md`](throughput.md). The throughput bench is a regression guard for the framework's per-event overhead on a known Testcontainers substrate — it was **not** designed as a capacity-planning tool. Every number below carries at least ±50% uncertainty, and the multi-silo factor is the weakest link because Edict has no measured cross-silo coordination data yet. Treat this as "order of magnitude" only; commission a real run against your target substrate before sizing infrastructure.

## Baseline

From `docs/benchmarks/throughput.md` (2026-05-30, Git SHA `12b8229`), single-silo Orleans TestCluster on a 20-core Ryzen AI 9 365 / 64 GB / .NET 10.0.8 laptop, Testcontainers on the same host:

| Substrate                  | Open-loop sustained EPS | Notes                                                          |
| -------------------------- | ----------------------: | -------------------------------------------------------------- |
| `azure` (Azurite)          |                      62 | Azurite emulator dominates the per-op floor.                   |
| `kafkapostgres` (TC)       |                     819 | Single-broker Kafka + Postgres 17, all sharing host CPU & RAM. |

## Per-silo lift estimate (laptop → production)

### Azure (Azurite → real Azure Queue Storage)

- Azurite has materially higher per-op latency than real Azure Storage under load.
- Edict's defaults give 16 queues × 10 ms poll cadence with batched gets — the theoretical fan-out ceiling sits well above 1 k EPS per silo; Azurite is the dominant brake.
- **Honest lift: 5–10×. Call it ~400 EPS per silo on real Azure Storage.**

### Kafka + Postgres (Testcontainers → real cluster + managed Postgres)

- Single-broker Kafka and a single Postgres container, both sharing CPU with the silo, contend for the same 20 cores.
- Per event Edict writes ~3 rows to Postgres (idempotency + projection + grain state) plus the Kafka produce.
- **Honest lift: 3–5×. Call it ~3,000 EPS per silo on real Kafka + managed Postgres.**

## Multi-silo extrapolation

Applies a 0.75 efficiency factor per added silo to account for Orleans cross-silo coordination (idempotency grain placement, projection contention, stream-pulling-agent rebalancing). Production data may move this factor in either direction.

| Silos | Azure (real) EPS | Kafka + Postgres (real) EPS         |
| ----: | ---------------: | ----------------------------------: |
|     1 |             ~400 |                              ~3,000 |
|     2 |             ~600 |                              ~4,500 |
|     4 |           ~1,200 | ~9,000 (Postgres-bound — see below) |
|     8 |           ~2,400 | ~18,000 (DB-saturated without sharding) |

## Substrate ceilings — what binds each column

### Azure stream provider

- `NumQueues = 16` (`EdictAzureStreamsOptions`) — stream parallelism caps at 16 silos. At 8 silos you have 2 queues each, still headroom.
- Azure Storage account default: **20 k transactions/sec** per account (raisable via Azure support). At ~3 ops/event that's ~6,500 EPS — comfortably above the 8-silo estimate.
- **Binding constraint at 8 silos: silo CPU, not the substrate.** Scaling past 8 silos remains useful until you hit the 16-queue partition ceiling.

### Kafka + Postgres

- `PartitionCount = 32` per `[EdictStream]` (ADR-0028) — up to 32 silos consume in parallel before partition exhaustion.
- Kafka on a real 3-broker cluster comfortably does 100 k+ msg/sec for small messages — **not the bottleneck at any of these scales.**
- **Postgres is the binding constraint.** With ~3 writes/event, a 16 vCPU managed Postgres at ~20–30 k TPS sustained gives roughly **5,000–7,000 EPS** for Edict. 2 silos fit; **4 silos already brush the ceiling** at the new per-silo number, and 8 silos saturate it. To go past 2–3 silos on a 16 vCPU instance you need to:
  - vertically scale to a 32–64 vCPU instance (~10 k EPS for Edict — fits 3–4 silos), or
  - shard idempotency / projection storage across multiple Postgres instances.

## Assumptions worth pressure-testing

- **Workload weight.** The bench handler does a single counter increment. Production workloads with bigger projections, larger event payloads, or chained `Raise` calls will sit below these numbers.
- **Edict defaults.** `PartitionCount = 32`, `NumQueues = 16`, `QueuePollingPeriod = 10 ms`. Raising `NumQueues` for Azure or `PartitionCount` for Kafka changes the parallelism ceiling.
- **Coordination factor.** The 0.75 multi-silo efficiency is a textbook Orleans heuristic — not measured here. A workload with heavy grain-locality (e.g. one hot aggregate) will see much worse scaling; an evenly-sharded one may approach linear.
- **Cold-start and tail behaviour.** All EPS figures are steady-state after warmup. Cold start, deployment rollout, and reminder-driven retries are not modelled.

## What would tighten this estimate

- A one-off open-loop run against a **real Azure Storage account** (any tier) — kills the Azurite uncertainty in one measurement.
- A Postgres `pg_stat_statements` snapshot during a kafkapostgres bench run — confirms which write dominates and lets you predict the DB ceiling at any instance size.
- A 2-silo (and ideally 4-silo) variant of the bench — replaces the 0.75 efficiency guess with measured cross-silo coordination cost.

Until those land, treat this document as a sketch, not a quote.
