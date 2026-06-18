# Edict throughput

Machine: Microsoft Windows 10.0.26200 / 20 cores / AMD Ryzen AI 9 365 @ 2.0 GHz / 64 GB RAM
.NET version: 10.0.8
Azure run date: 2026-06-06
Kafka × Postgres run date: 2026-06-06
Git SHA: 31cfa0b

> **Read this first.** The substrates are Testcontainers (Azurite, Postgres, Kafka) on the same laptop as the silo process. Containers share host CPU and RAM with the producer/consumer Orleans process; no resource caps are set, so Docker defaults apply. The reported machine class is what the .NET process sees, not what each substrate sees in isolation. **Do not read these numbers as "Edict will do X EPS in production"** — a managed Postgres, a real Kafka cluster, or an Azure Storage account would all change the substrate ceiling independently of any Edict code change. The bench is a regression guard for the *framework's* per-event overhead on a known substrate, not a sizing tool for your deployment.

## System throughput (sustained, end-to-end)

Open-loop Events workload: N=256 producers fire `SendAsync(...)` as fast as they can for 30 s, after a 20 s warmup that lets JIT, grain caches, idempotency rings and the stream pulling agents reach steady state. The reported figure is a single sum of per-aggregate counters read once at window-end, divided by 30 s — no per-event polling, no drain detection. Read this as the rate the substrate's consumer can absorb when the producer is not paced by the consumer; your own workload will only touch this ceiling if its per-event work is no heavier than the bench's counter increment. Saturation runs against the same Testcontainers substrate as the closed-loop sweeps — a real Postgres / Kafka / Azure Storage backend will sit at a different ceiling, generally higher.

Each substrate is measured twice, once per **projection species**, running the identical per-aggregate counter workload so the only thing that varies is where the read model is stored:

- **List (external store)** — the counter lives in an external keyed store, drained at-least-once by an `UpsertRow` outbox effect after each event.
- **State (in-grain)** — the counter lives in the grain's own durable state and commits **inline** with the dedup ring in a single write: no external store, no outbox effect.

The EPS delta between a substrate's two rows is the storage-commit cost in isolation. The in-grain species is expected to sit higher: it folds the read-model write into the one state write the idempotency ring already makes, rather than staging and draining a second write.

| Substrate | Projection | Events / sec (end-to-end) | Health |
| --- | --- | ---: | :---: |
| azure | List (external store) | 66 | OK (0.00 %) |
| azure | State (in-grain) | 80 | OK (0.00 %) |
| kafkapostgres | List (external store) | 792 | OK (0.00 %) |
| kafkapostgres | State (in-grain) | 1482 | OK (0.00 %) |

> **Read the delta, not the absolutes.** The in-grain advantage shown here is the per-event commit saving on a small, hot, per-aggregate counter — the case the State species exists for. It is not a blanket "grain state is faster": a large read model in grain state inflates every activation of that grain, because the whole payload is read and written on each turn, which the external List store avoids. Choose State for small per-aggregate read models and List for large or unbounded ones; this table prices the commit, not the activation.

> **Per-silo baseline.** The published number is the rate **one** Orleans silo sustains on this hardware against the configured substrate. Orleans scales horizontally; an N-silo deployment extrapolates from this baseline modulo cross-silo coordination cost. A single-silo number is not the framework ceiling.

## Per-event latency (closed-loop)

Closed-loop sweep across `N ∈ {2, 16, 64}` issuer tasks, two scenarios per substrate, 10 s warmup + 30 s measurement window. **No EPS column** here — closed-loop's bounded `await` rate-paces the producer, so any per-second figure would read as a throughput claim it cannot make. The full closed-loop EPS surface is preserved in the raw CSV alongside per-sample latency.

- **Command acceptance** — `SendAsync` round-trip, handler increments durable state and returns `Accepted`. No `Raise`, no stream hop, no projection.
- **Command → Event delivery** — `SendAsync` + handler `Raise` + stream hop + consumer dispatch + projection write, with completion signalled by a 5 ms point-get poll on the projection row.

| Substrate | Scenario | Parallelism | p50 (ms) | p95 (ms) | p99 (ms) | Health |
| --- | --- | --- | ---: | ---: | ---: | :---: |
| azure | Command acceptance | 2 | 23.77 | 34.51 | 42.73 | OK (0.00 %) |
| azure | Command acceptance | 16 | 74.02 | 93.23 | 179.55 | OK (0.00 %) |
| azure | Command acceptance | 64 | 288.37 | 329.09 | 420.17 | OK (0.00 %) |
| azure | Command → Event delivery | 2 | 77.12 | 97.55 | 116.65 | OK (0.00 %) |
| azure | Command → Event delivery | 16 | 292.48 | 428.30 | 520.95 | OK (0.00 %) |
| azure | Command → Event delivery | 64 | 1218.28 | 1449.34 | 1576.85 | OK (0.00 %) |
| kafkapostgres | Command acceptance | 2 | 1.73 | 2.45 | 3.58 | OK (0.00 %) |
| kafkapostgres | Command acceptance | 16 | 6.02 | 8.43 | 11.82 | OK (0.00 %) |
| kafkapostgres | Command acceptance | 64 | 20.70 | 27.69 | 31.78 | OK (0.00 %) |
| kafkapostgres | Command → Event delivery | 2 | 128.82 | 194.78 | 209.15 | OK (0.00 %) |
| kafkapostgres | Command → Event delivery | 16 | 110.71 | 176.13 | 191.43 | OK (0.00 %) |
| kafkapostgres | Command → Event delivery | 64 | 172.78 | 257.42 | 280.99 | OK (0.00 %) |

## Run health

All sweep points completed under the 1% failure-rate threshold.

## Setup

- Both substrates measured on the same machine and the same .NET runtime, one day apart, both registered through `Edict.Benchmarks.Throughput` via `SubstrateRegistry`.
- Substrates are Testcontainers running on the same host as the silo process — they share CPU, RAM, and the local loopback with everything else the bench does. No container resource caps are set; Docker defaults apply. A real managed substrate (Azure Storage, Aiven Kafka, Cloud SQL) would not have these contention or latency characteristics.
- Single Orleans TestCluster silo per substrate run (producer and consumers share one process).
- Edict tunables in effect, all framework defaults — no bench-side overrides:
  - `PartitionCount = 32` (ADR-0028) — Kafka substrate, `[EdictStream]`-level partition count.
  - `NumQueues = 16` (`EdictAzureStreamsOptions`) — Azure substrate, pulling-agent fan-out.
  - `QueuePollingPeriod = 10 ms` (`EdictAzureStreamsOptions`) — Azure substrate, consumer-side poll cadence.
- Single run per substrate on dev hardware; expect ±20% variance run-to-run. Numbers are a baseline for the registered defaults of each substrate, not a framework ceiling.

### Bring-up tuning

The bench stands fresh Testcontainers up once per pass (closed-loop, then the two saturation species) per substrate, so it lowers the stock Testcontainers waits and times out fast into a fresh-container retry rather than hanging on a stalled port mapping. Each knob is read from an environment variable at start-up, falling back to the default below if the variable is absent or malformed. Override them only if a genuinely slow machine is failing bring-up on the defaults.

| Environment variable | Default | What it bounds |
|---|---|---|
| `EDICT_BENCH_TESTCONTAINERS_WAIT_SECONDS` | `90` | In-container readiness wait handed to Testcontainers, replacing its silent ~1 h default. |
| `EDICT_BENCH_HOST_PROBE_DEADLINE_SECONDS` | `30` | Host-side TCP-connect readiness probe deadline against the container's mapped ports. |
| `EDICT_BENCH_HOST_PROBE_POLL_MS` | `250` | Poll cadence (milliseconds) the host-readiness probe retries its connect on. |
| `EDICT_BENCH_BRINGUP_STAGGER_SECONDS` | `2` | Gap between successive within-boot steps (e.g. Postgres then Kafka) so heavy starts don't fight for CPU and disk. |
| `EDICT_BENCH_BRINGUP_SETTLE_SECONDS` | `5` | Cross-boot settle gap between a pass's teardown and the next pass's bring-up, letting the port-forwarder drain. |
| `EDICT_BENCH_BRINGUP_RETRIES` | `3` | Number of dispose-and-recreate fresh-container bring-up attempts before failing the run. |
| `EDICT_BENCH_SETUP_DEADLINE_SECONDS` | `300` | Whole-pass setup budget (container bring-up + silo deploy); a breach fails the run fast, naming the substrate and phase. |

## What you're looking at — `azure` (Azurite + Azure Queue streams)

Azurite emulator, single Orleans silo, producer and consumers in one process. Three substrate ceilings, not framework ceilings:

- Azurite's per-op latency floor is materially above real Azure Storage.
- The Azure Queue stream provider polls on a fixed timer (`EdictAzureStreamsOptions.QueuePollingPeriod`); at high parallelism the `Command → Event delivery` row sits on that floor.
- One silo serialises everything; Orleans scales horizontally and these numbers don't exercise that.

Treat the table as registered defaults on a laptop emulator. A real Azure Storage account, a tuned poll period, or a multi-silo deployment moves the numbers up independently of any framework change.

## What you're looking at — `kafkapostgres` (Testcontainers Kafka + Postgres)

`Edict.Kafka` (custom `IQueueAdapter` over `Confluent.Kafka`, ADR-0028) + `Edict.Postgres` persistence. Testcontainers Kafka broker + Postgres 17, same single silo, same `BenchAggregateHandler` workload, same per-send `ConversationId`-keyed completion poll as `azure`. A single-broker container under Docker defaults is the relevant ceiling here, not a multi-broker Kafka cluster on dedicated hardware.

- Producer: `acks=all`, idempotent, lz4. Consumer: `enable.auto.commit=false`, manual commit after `HandleAsync` (ADR-0028 §2).
- `PartitionCount = 32` per `[EdictStream]` — Edict's framework default (ADR-0028), inherited by the bench substrate.
