# Edict production-scale estimate

> **Read this first.** This document is a back-of-envelope sizing sketch derived from the laptop benchmarks in [`throughput.md`](throughput.md). The throughput bench is a regression guard for the framework's per-event overhead on a known Testcontainers substrate — it was **not** designed as a capacity-planning tool. Every number below carries at least ±50% uncertainty, and the multi-silo factor is the weakest link because Edict has no measured cross-silo coordination data yet. Treat this as "order of magnitude" only; commission a real run against your target substrate before sizing infrastructure.

## Baseline

From `docs/benchmarks/throughput.md` (2026-06-19, Git SHA `2cb8187`), single-silo Orleans TestCluster on a 20-core Ryzen AI 9 365 / 64 GB / .NET 10.0.8 laptop, Testcontainers on the same host. Each substrate is measured twice, once per **projection species**, against the identical per-aggregate counter workload — so the only thing that varies between the two rows is where the read model is stored:

| Substrate            | Projection            | Open-loop sustained EPS | Notes                                                          |
| -------------------- | --------------------- | ----------------------: | -------------------------------------------------------------- |
| `azure` (Azurite)    | List (external store) |                      60 | Azurite emulator dominates the per-op floor either way.        |
| `azure` (Azurite)    | State (in-grain)      |                      78 | In-grain commit; species delta is small because Azurite binds. |
| `kafkapostgres` (TC) | List (external store) |                     475 | Single-broker Kafka + Postgres 17, all sharing host CPU & RAM. |
| `kafkapostgres` (TC) | State (in-grain)      |                   3,183 | One combined write instead of a dedup-ring write + outbox drain. |

**The projection species is a first-class sizing lever, not a footnote.** The State (in-grain) species folds the read-model write into the single durable write the idempotency ring already makes; the List (external store) species stages a second write and drains it at-least-once through an `UpsertRow` outbox effect. On kafkapostgres that lifts throughput **~6.7×** (475 → 3,183 EPS) **and** roughly halves per-event write amplification. That gap is far wider than write amplification (~2×) alone explains: the List drain path adds extra stream hops and grain turns on top of the second write, and that async work is penalised disproportionately when broker, database, and silo all contend for the same laptop cores. Expect the gap to **compress on dedicated infrastructure**, where the drain path is not fighting the silo for CPU; treat 6.7× as the contended-laptop ceiling of the delta, not a stable production multiplier. On azure the delta is small because Azurite's per-op latency floor binds before the commit cost does.

> The State species exists for **small, hot, per-aggregate read models**. A large read model in grain state inflates every activation of that grain (the whole payload is read and written each turn), which the external List store avoids. The numbers above price the *commit*, not the activation — choose State for small per-aggregate counters and List for large or unbounded read models.

## Per-silo lift estimate (laptop → production)

### Azure (Azurite → real Azure Queue Storage)

- Azurite has materially higher per-op latency than real Azure Storage under load, and binds before the projection-species commit cost does — which is why List (60) and State (78) sit so close together here.
- Edict's defaults give 16 queues × 10 ms poll cadence with batched gets — the theoretical fan-out ceiling sits well above 1 k EPS per silo; Azurite is the dominant brake.
- **Honest lift: 5–10×. Call it ~400–500 EPS per silo on real Azure Storage**, with the two species converging once the emulator floor is removed.

### Kafka + Postgres (Testcontainers → real cluster + managed Postgres)

- Single-broker Kafka and a single Postgres container, both sharing CPU with the silo, contend for the same 20 cores.
- Write amplification depends on the projection species: **List writes ~2 rows/event** (the dedup-ring state write plus the external `UpsertRow` drain); **State writes ~1 row/event** (dedup ring and counter commit together).
- **The two species have different headroom, so they take different lifts** — this is a judgement call, not a measurement:
  - **List (~475 EPS laptop): ~3–4× → ~1,800 EPS per silo.** The async outbox-drain path is the thing host contention crushes hardest, so it has the most to gain once broker, DB, and silo stop fighting for cores.
  - **State (~3,183 EPS laptop): ~2–3× → ~8,000 EPS per silo.** A smaller multiplier on purpose: at 3,183 EPS the laptop is already pushing ~3,200 inline write-TPS at a Postgres container that can do far more, so State is closer to **silo-CPU-bound than substrate-bound** even on the laptop. Dedicated cores help, but there is less contention to remove than List has — so do not naively apply List's multiplier to it.

## Multi-silo extrapolation

Applies a 0.75 efficiency factor per added silo to account for Orleans cross-silo coordination (idempotency grain placement, projection contention, stream-pulling-agent rebalancing). Production data may move this factor in either direction. The kafkapostgres column is shown for the **State** species (the recommended path for per-aggregate counters); the List species sits at roughly a quarter of these (~1,800 EPS/silo), not half — the single-silo species delta is ~6.7× on this hardware, far more than write amplification alone.

| Silos | Azure (real) EPS | Kafka + Postgres (real, State) EPS    |
| ----: | ---------------: | ------------------------------------: |
|     1 |             ~500 |                                ~8,000 |
|     2 |             ~750 |                               ~12,000 |
|     4 |           ~1,500 |  ~24,000 (Postgres-bound — see below) |
|     8 |           ~3,000 | ~48,000 (DB-saturated without sharding) |

## Substrate ceilings — what binds each column

### Azure stream provider

- `NumQueues = 16` (`EdictAzureStreamsOptions`) — stream parallelism caps at 16 silos. At 8 silos you have 2 queues each, still headroom.
- Azure Storage account default: **20 k transactions/sec** per account (raisable via Azure support). At ~2 ops/event (List) that's ~10 k EPS; the State species, writing ~1 op/event, sits higher still — comfortably above the 8-silo estimate.
- **Binding constraint at 8 silos: silo CPU, not the substrate.** Scaling past 8 silos remains useful until you hit the 16-queue partition ceiling.

### Kafka + Postgres

- `PartitionCount = 32` per `[EdictStream]` (ADR-0028) — up to 32 silos consume in parallel before partition exhaustion.
- Kafka on a real 3-broker cluster comfortably does 100 k+ msg/sec for small messages — **not the bottleneck at any of these scales.**
- **Postgres is the binding constraint — but the species that saturates it *first* is the counter-intuitive one.** A 16 vCPU managed Postgres sustains ~20–30 k write-TPS. What matters is write-TPS per silo (EPS/silo × writes/event), and State's much higher per-silo throughput more than cancels its lower per-event write count:
  - **State (~1 write/event, ~8,000 EPS/silo → ~8 k write-TPS/silo):** a single 16 vCPU instance saturates at roughly **3–4 silos** (~24–30 k EPS total). State is so much faster *per silo* that it reaches the shared-DB ceiling at **fewer** silos than List, even though it is lighter per event.
  - **List (~2 writes/event, ~1,800 EPS/silo → ~3.6 k write-TPS/silo):** the same instance takes roughly **6–8 silos** to saturate (~10–14 k EPS total).
  - To push past these silo counts on a 16 vCPU instance, either vertically scale to a 32–64 vCPU instance, or shard idempotency / projection storage across multiple Postgres instances. **State still wins on cost-per-event** (lower write amplification and far higher single-silo throughput); the trade is that it concentrates that throughput, so you reach for DB scaling at a lower silo count.

## Assumptions worth pressure-testing

- **Workload weight.** The bench handler does a single counter increment. Production workloads with bigger projections, larger event payloads, or chained `Raise` calls will sit below these numbers. A large in-grain read model also erodes the State-species advantage (activation cost climbs).
- **Edict defaults.** `PartitionCount = 32`, `NumQueues = 16`, `QueuePollingPeriod = 10 ms`. Raising `NumQueues` for Azure or `PartitionCount` for Kafka changes the parallelism ceiling.
- **Projection species.** The State-vs-List split is measured single-silo on this laptop, where State runs ~6.7× the List throughput (3,183 vs 475 EPS) — far more than the ~2× write-amplification difference. The excess is the List outbox-drain path being penalised by host contention, so the delta likely **compresses on dedicated infrastructure**. Treating 6.7× as a stable production multiplier is the most aggressive extrapolation in this document; the per-silo lifts above deliberately narrow it (List takes a larger lift than State).
- **Coordination factor.** The 0.75 multi-silo efficiency is a textbook Orleans heuristic — not measured here. A workload with heavy grain-locality (e.g. one hot aggregate) will see much worse scaling; an evenly-sharded one may approach linear.
- **Cold-start and tail behaviour.** All EPS figures are steady-state after warmup. Cold start, deployment rollout, and reminder-driven retries are not modelled.

## What would tighten this estimate

- A one-off open-loop run against a **real Azure Storage account** (any tier) — kills the Azurite uncertainty in one measurement and shows whether List and State really do converge there.
- A Postgres `pg_stat_statements` snapshot during a kafkapostgres bench run, taken once per species — confirms the ~2-writes-vs-~1-write amplification and lets you predict the DB ceiling at any instance size.
- A 2-silo (and ideally 4-silo) variant of the bench — replaces the 0.75 efficiency guess with measured cross-silo coordination cost.

Until those land, treat this document as a sketch, not a quote.
