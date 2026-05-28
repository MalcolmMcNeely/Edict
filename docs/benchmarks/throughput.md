# Edict throughput

Machine: Microsoft Windows 10.0.26200 / 20 cores
.NET version: 10.0.8
Azure run date: 2026-05-28
Kafka × Postgres run date: 2026-05-28
Git SHA: 53f0a00

## System throughput (sustained, end-to-end)

Open-loop Events workload: N=256 producers fire `Send(...)` as fast as they can for 30 s, after a 20 s warmup that lets JIT, grain caches, idempotency rings and the stream pulling agents reach steady state. The reported figure is a single sum of per-aggregate counters read once at window-end, divided by 30 s — no per-event polling, no drain detection. Read this as the rate the substrate's consumer can absorb when the producer is not paced by the consumer; your own workload will only touch this ceiling if its per-event work is no heavier than the bench's counter increment.

| Substrate | Events / sec (end-to-end) |
| --- | ---: |
| azure | 55 |
| kafkapostgres | 102 |

> **Per-silo baseline.** The published number is the rate **one** Orleans silo sustains on this hardware against the configured substrate. Orleans scales horizontally; an N-silo deployment extrapolates from this baseline modulo cross-silo coordination cost. A single-silo number is not the framework ceiling.

## Per-event latency (closed-loop)

Closed-loop sweep across `N ∈ {2, 16, 64}` issuer tasks, two scenarios per substrate, 10 s warmup + 30 s measurement window. **No EPS column** here — closed-loop's bounded `await` rate-paces the producer, so any per-second figure would read as a throughput claim it cannot make. The full closed-loop EPS surface is preserved in the raw CSV alongside per-sample latency.

- **Command acceptance** — `Send` round-trip, handler increments durable state and returns `Accepted`. No `Raise`, no stream hop, no projection.
- **Command → Event delivery** — `Send` + handler `Raise` + stream hop + consumer dispatch + projection write, with completion signalled by a 5 ms point-get poll on the projection row.

| Substrate | Scenario | Parallelism | p50 (ms) | p95 (ms) | p99 (ms) |
| --- | --- | --- | ---: | ---: | ---: |
| azure | Command acceptance | 2 | 64.19 | 85.92 | 103.03 |
| azure | Command acceptance | 16 | 68.74 | 100.27 | 137.19 |
| azure | Command acceptance | 64 | 168.31 | 237.29 | 295.56 |
| azure | Command → Event delivery | 2 | 431.11 | 540.72 | 686.79 |
| azure | Command → Event delivery | 16 | 879.89 | 1053.36 | 1160.92 |
| azure | Command → Event delivery | 64 | 1919.57 | 2906.28 | 3066.22 |
| kafkapostgres | Command acceptance | 2 | 20.09 | 46.51 | 60.69 |
| kafkapostgres | Command acceptance | 16 | 31.35 | 52.94 | 66.77 |
| kafkapostgres | Command acceptance | 64 | 90.49 | 121.81 | 139.22 |
| kafkapostgres | Command → Event delivery | 2 | 327.61 | 412.62 | 469.23 |
| kafkapostgres | Command → Event delivery | 16 | 370.97 | 22923.98 | 25644.00 |
| kafkapostgres | Command → Event delivery | 64 | 977.66 | 21970.76 | 27868.10 |

## Setup

- Both substrates measured on the same machine and the same .NET runtime, one day apart, both registered through `Edict.Benchmarks.Throughput` via `SubstrateRegistry`.
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

Treat the table as registered defaults on a laptop emulator. A real storage account, a tuned poll period, or a multi-silo deployment moves the numbers up independently of any framework change.

## What you're looking at — `kafkapostgres` (Testcontainers Kafka + Postgres)

`Edict.Kafka` (custom `IQueueAdapter` over `Confluent.Kafka`, ADR-0028) + `Edict.Postgres` persistence. Testcontainers Kafka broker + Postgres 16, same single silo, same `BenchAggregateHandler` workload, same per-send `CorrelationId`-keyed completion poll as `azure`.

- Producer: `acks=all`, idempotent, lz4. Consumer: `enable.auto.commit=false`, manual commit after `HandleAsync` (ADR-0028 §2).
- `PartitionCount = 32` per `[EdictStream]` — Edict's framework default (ADR-0028), inherited by the bench substrate.
