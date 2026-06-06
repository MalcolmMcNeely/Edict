# Edict production-scale estimate

> **Read this first.** This document is a back-of-envelope sizing sketch derived from the laptop benchmarks in [`throughput.md`](throughput.md). The throughput bench is a regression guard for the framework's per-event overhead on a known Testcontainers substrate — it was **not** designed as a capacity-planning tool. Every number below carries at least ±50% uncertainty, and the multi-silo factor is the weakest link because Edict has no measured cross-silo coordination data yet. Treat this as "order of magnitude" only; commission a real run against your target substrate before sizing infrastructure.

## Baseline

From `docs/benchmarks/throughput.md` (2026-06-06, Git SHA `31cfa0b`), single-silo Orleans TestCluster on a 20-core Ryzen AI 9 365 / 64 GB / .NET 10.0.8 laptop, Testcontainers on the same host. Each substrate is measured twice, once per **projection species**, against the identical per-aggregate counter workload — so the only thing that varies between the two rows is where the read model is stored:

| Substrate            | Projection            | Open-loop sustained EPS | Notes                                                          |
| -------------------- | --------------------- | ----------------------: | -------------------------------------------------------------- |
| `azure` (Azurite)    | List (external store) |                      66 | Azurite emulator dominates the per-op floor either way.        |
| `azure` (Azurite)    | State (in-grain)      |                      80 | In-grain commit; species delta is small because Azurite binds. |
| `kafkapostgres` (TC) | List (external store) |                     792 | Single-broker Kafka + Postgres 17, all sharing host CPU & RAM. |
| `kafkapostgres` (TC) | State (in-grain)      |                   1,482 | One combined write instead of a dedup-ring write + outbox drain. |

**The projection species is a first-class sizing lever, not a footnote.** The State (in-grain) species folds the read-model write into the single durable write the idempotency ring already makes; the List (external store) species stages a second write and drains it at-least-once through an `UpsertRow` outbox effect. On kafkapostgres that nearly doubles throughput (792 → 1,482 EPS) **and** roughly halves per-event write amplification — both of which push the Postgres-bound ceiling further out (see below). On azure the delta is small because Azurite's per-op latency floor binds before the commit cost does.

> The State species exists for **small, hot, per-aggregate read models**. A large read model in grain state inflates every activation of that grain (the whole payload is read and written each turn), which the external List store avoids. The numbers above price the *commit*, not the activation — choose State for small per-aggregate counters and List for large or unbounded read models.

## Per-silo lift estimate (laptop → production)

### Azure (Azurite → real Azure Queue Storage)

- Azurite has materially higher per-op latency than real Azure Storage under load, and binds before the projection-species commit cost does — which is why List (66) and State (80) sit so close together here.
- Edict's defaults give 16 queues × 10 ms poll cadence with batched gets — the theoretical fan-out ceiling sits well above 1 k EPS per silo; Azurite is the dominant brake.
- **Honest lift: 5–10×. Call it ~400–500 EPS per silo on real Azure Storage**, with the two species converging once the emulator floor is removed.

### Kafka + Postgres (Testcontainers → real cluster + managed Postgres)

- Single-broker Kafka and a single Postgres container, both sharing CPU with the silo, contend for the same 20 cores.
- Write amplification depends on the projection species: **List writes ~2 rows/event** (the dedup-ring state write plus the external `UpsertRow` drain); **State writes ~1 row/event** (dedup ring and counter commit together).
- **Honest lift: 3–5×.** Anchoring on the species you ship:
  - **List: ~3,000 EPS per silo** on real Kafka + managed Postgres.
  - **State: ~5,000 EPS per silo** — faster *and* lighter on the database.

## Multi-silo extrapolation

Applies a 0.75 efficiency factor per added silo to account for Orleans cross-silo coordination (idempotency grain placement, projection contention, stream-pulling-agent rebalancing). Production data may move this factor in either direction. The kafkapostgres column is shown for the **State** species (the recommended path for per-aggregate counters); halve it for the List species.

| Silos | Azure (real) EPS | Kafka + Postgres (real, State) EPS    |
| ----: | ---------------: | ------------------------------------: |
|     1 |             ~500 |                                ~5,000 |
|     2 |             ~750 |                                ~7,500 |
|     4 |           ~1,500 |  ~15,000 (Postgres-bound for List — see below) |
|     8 |           ~3,000 | ~30,000 (DB-saturated without sharding) |

## Substrate ceilings — what binds each column

### Azure stream provider

- `NumQueues = 16` (`EdictAzureStreamsOptions`) — stream parallelism caps at 16 silos. At 8 silos you have 2 queues each, still headroom.
- Azure Storage account default: **20 k transactions/sec** per account (raisable via Azure support). At ~2 ops/event (List) that's ~10 k EPS; the State species, writing ~1 op/event, sits higher still — comfortably above the 8-silo estimate.
- **Binding constraint at 8 silos: silo CPU, not the substrate.** Scaling past 8 silos remains useful until you hit the 16-queue partition ceiling.

### Kafka + Postgres

- `PartitionCount = 32` per `[EdictStream]` (ADR-0028) — up to 32 silos consume in parallel before partition exhaustion.
- Kafka on a real 3-broker cluster comfortably does 100 k+ msg/sec for small messages — **not the bottleneck at any of these scales.**
- **Postgres is the binding constraint, and the projection species sets where it sits.** A 16 vCPU managed Postgres sustains ~20–30 k write-TPS:
  - **List (~2 writes/event):** roughly **10,000–15,000 EPS** for Edict. 2 silos fit comfortably; **4 silos brush the ceiling**; 8 silos saturate it.
  - **State (~1 write/event):** roughly **20,000–30,000 EPS** for Edict. The DB ceiling moves out by ~2× versus List — the per-silo throughput gain compounds with the lower write amplification.
  - To push the List species past 2–3 silos on a 16 vCPU instance (or the State species past ~4–6), either vertically scale to a 32–64 vCPU instance, or shard idempotency / projection storage across multiple Postgres instances. **Switching small per-aggregate read models to the State species is the cheapest lever** before reaching for either.

## Assumptions worth pressure-testing

- **Workload weight.** The bench handler does a single counter increment. Production workloads with bigger projections, larger event payloads, or chained `Raise` calls will sit below these numbers. A large in-grain read model also erodes the State-species advantage (activation cost climbs).
- **Edict defaults.** `PartitionCount = 32`, `NumQueues = 16`, `QueuePollingPeriod = 10 ms`. Raising `NumQueues` for Azure or `PartitionCount` for Kafka changes the parallelism ceiling.
- **Projection species.** The State-vs-List split is measured single-silo on this laptop; the claim that its ~2× per-event advantage holds at production scale is an extrapolation, not a measurement.
- **Coordination factor.** The 0.75 multi-silo efficiency is a textbook Orleans heuristic — not measured here. A workload with heavy grain-locality (e.g. one hot aggregate) will see much worse scaling; an evenly-sharded one may approach linear.
- **Cold-start and tail behaviour.** All EPS figures are steady-state after warmup. Cold start, deployment rollout, and reminder-driven retries are not modelled.

## What would tighten this estimate

- A one-off open-loop run against a **real Azure Storage account** (any tier) — kills the Azurite uncertainty in one measurement and shows whether List and State really do converge there.
- A Postgres `pg_stat_statements` snapshot during a kafkapostgres bench run, taken once per species — confirms the ~2-writes-vs-~1-write amplification and lets you predict the DB ceiling at any instance size.
- A 2-silo (and ideally 4-silo) variant of the bench — replaces the 0.75 efficiency guess with measured cross-silo coordination cost.

Until those land, treat this document as a sketch, not a quote.
