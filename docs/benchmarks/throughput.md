# Edict throughput

Machine: Microsoft Windows 10.0.26200 / 20 cores / AMD Ryzen AI 9 365 @ 2.0 GHz / 64 GB RAM
.NET version: 10.0.8
Azure run date: 2026-05-29
Kafka × Postgres run date: 2026-05-29
Git SHA: b4573ee

> **Read this first.** The substrates are Testcontainers (Azurite, Postgres, Kafka) on the same laptop as the silo process. Containers share host CPU and RAM with the producer/consumer Orleans process; no resource caps are set, so Docker defaults apply. The reported machine class is what the .NET process sees, not what each substrate sees in isolation. **Do not read these numbers as "Edict will do X EPS in production"** — a managed Postgres, a real Kafka cluster, or an Azure Storage account would all change the substrate ceiling independently of any Edict code change. The bench is a regression guard for the *framework's* per-event overhead on a known substrate, not a sizing tool for your deployment.

## System throughput (sustained, end-to-end)

Open-loop Events workload: N=256 producers fire `Send(...)` as fast as they can for 30 s, after a 20 s warmup that lets JIT, grain caches, idempotency rings and the stream pulling agents reach steady state. The reported figure is a single sum of per-aggregate counters read once at window-end, divided by 30 s — no per-event polling, no drain detection. Read this as the rate the substrate's consumer can absorb when the producer is not paced by the consumer; your own workload will only touch this ceiling if its per-event work is no heavier than the bench's counter increment. Saturation runs against the same Testcontainers substrate as the closed-loop sweeps — a real Postgres / Kafka / Azure Storage backend will sit at a different ceiling, generally higher.

| Substrate | Events / sec (end-to-end) | Health |
| --- | ---: | :---: |
| azure | 70 | OK (0.00 %) |
| kafkapostgres | 373 | OK (0.00 %) |

> **Per-silo baseline.** The published number is the rate **one** Orleans silo sustains on this hardware against the configured substrate. Orleans scales horizontally; an N-silo deployment extrapolates from this baseline modulo cross-silo coordination cost. A single-silo number is not the framework ceiling.

## Per-event latency (closed-loop)

Closed-loop sweep across `N ∈ {2, 16, 64}` issuer tasks, two scenarios per substrate, 10 s warmup + 30 s measurement window. **No EPS column** here — closed-loop's bounded `await` rate-paces the producer, so any per-second figure would read as a throughput claim it cannot make. The full closed-loop EPS surface is preserved in the raw CSV alongside per-sample latency.

- **Command acceptance** — `Send` round-trip, handler increments durable state and returns `Accepted`. No `Raise`, no stream hop, no projection.
- **Command → Event delivery** — `Send` + handler `Raise` + stream hop + consumer dispatch + projection write, with completion signalled by a 5 ms point-get poll on the projection row.

| Substrate | Scenario | Parallelism | p50 (ms) | p95 (ms) | p99 (ms) | Health |
| --- | --- | --- | ---: | ---: | ---: | :---: |
| azure | Command acceptance | 2 | 11.95 | 18.56 | 25.52 | OK (0.00 %) |
| azure | Command acceptance | 16 | 36.58 | 57.11 | 141.14 | OK (0.00 %) |
| azure | Command acceptance | 64 | 144.73 | 187.36 | 353.11 | OK (0.00 %) |
| azure | Command → Event delivery | 2 | 77.42 | 98.10 | 114.85 | OK (0.00 %) |
| azure | Command → Event delivery | 16 | 277.55 | 362.74 | 405.43 | OK (0.00 %) |
| azure | Command → Event delivery | 64 | 1149.58 | 1251.14 | 1398.65 | OK (0.00 %) |
| kafkapostgres | Command acceptance | 2 | 1.84 | 4.66 | 7.46 | OK (0.00 %) |
| kafkapostgres | Command acceptance | 16 | 6.41 | 11.81 | 18.14 | OK (0.00 %) |
| kafkapostgres | Command acceptance | 64 | 24.39 | 34.48 | 46.83 | OK (0.00 %) |
| kafkapostgres | Command → Event delivery | 2 | 143.25 | 220.56 | 236.84 | OK (0.00 %) |
| kafkapostgres | Command → Event delivery | 16 | 142.45 | 216.01 | 244.16 | OK (0.00 %) |
| kafkapostgres | Command → Event delivery | 64 | 319.15 | 446.78 | 507.62 | OK (0.00 %) |

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

## What you're looking at — `azure` (Azurite + Azure Queue streams)

Azurite emulator, single Orleans silo, producer and consumers in one process. Three substrate ceilings, not framework ceilings:

- Azurite's per-op latency floor is materially above real Azure Storage.
- The Azure Queue stream provider polls on a fixed timer (`EdictAzureStreamsOptions.QueuePollingPeriod`); at high parallelism the `Command → Event delivery` row sits on that floor.
- One silo serialises everything; Orleans scales horizontally and these numbers don't exercise that.

Treat the table as registered defaults on a laptop emulator. A real Azure Storage account, a tuned poll period, or a multi-silo deployment moves the numbers up independently of any framework change.

## What you're looking at — `kafkapostgres` (Testcontainers Kafka + Postgres)

`Edict.Kafka` (custom `IQueueAdapter` over `Confluent.Kafka`, ADR-0028) + `Edict.Postgres` persistence. Testcontainers Kafka broker + Postgres 17, same single silo, same `BenchAggregateHandler` workload, same per-send `CorrelationId`-keyed completion poll as `azure`. A single-broker container under Docker defaults is the relevant ceiling here, not a multi-broker Kafka cluster on dedicated hardware.

- Producer: `acks=all`, idempotent, lz4. Consumer: `enable.auto.commit=false`, manual commit after `HandleAsync` (ADR-0028 §2).
- `PartitionCount = 32` per `[EdictStream]` — Edict's framework default (ADR-0028), inherited by the bench substrate.
