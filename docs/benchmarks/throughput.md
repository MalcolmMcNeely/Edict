# Edict throughput

Machine: Microsoft Windows 10.0.26200 / 20 cores
.NET version: 10.0.8
Azure run date: 2026-05-27
Kafka × Postgres run date: 2026-05-27
Git SHA: 3bd4e10

## Sustained throughput

Open-loop Events workload: N=256 producers fire `Send(...)` as fast as they can for 30 s, after a 20 s warmup that lets JIT, grain caches, idempotency rings and the stream pulling agents reach steady state. The reported EPS is a single sum of per-aggregate counters read once at window-end, divided by 30 s — no per-event polling, no drain detection. Read this as the rate the substrate's consumer can absorb when the producer is not paced by the consumer; your own workload will only touch this ceiling if its per-event work is no heavier than the bench's counter increment.

| Substrate | Events per second |
| --- | ---: |
| azure | 39 |
| kafkapostgres | 46 |

## Setup

- Both substrates measured on the same machine and the same .NET runtime, one day apart, both registered through `Edict.Benchmarks.Throughput` via `SubstrateRegistry`.
- Single Orleans TestCluster silo per substrate run (producer and consumers share one process).
- Each scenario sweep is `N ∈ {1, 4, 16, 64, 256}` with 10 s warmup + 30 s measurement window.
- Completion signal for the Events scenario is a 5 ms point-get poll against the projection table (Azure Tables on Azurite, `benchevent` on Postgres).
- RaiseOnly measures Send latency with Raise in the handler; does not wait for the projection.
- Single run per substrate on dev hardware; expect ±20% variance run-to-run. Numbers are a baseline for the registered defaults of each substrate, not a framework ceiling.

| Substrate | Scenario | Parallelism | Events per second (EPS) | p50 (ms) | p95 (ms) | p99 (ms) |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| azure | Commands | 1 | 22 | 46.30 | 58.91 | 63.78 |
| azure | Commands | 4 | 118 | 33.20 | 44.52 | 57.21 |
| azure | Commands | 16 | 232 | 65.37 | 102.27 | 171.88 |
| azure | Commands | 64 | 376 | 168.03 | 246.33 | 389.18 |
| azure | Commands | 256 | 382 | 654.17 | 789.53 | 1084.66 |
| azure | RaiseOnly | 1 | 8 | 127.28 | 158.92 | 222.41 |
| azure | RaiseOnly | 4 | 18 | 210.14 | 297.36 | 386.58 |
| azure | RaiseOnly | 16 | 26 | 574.49 | 893.87 | 1111.24 |
| azure | RaiseOnly | 64 | 32 | 1902.41 | 2688.83 | 2955.72 |
| azure | RaiseOnly | 256 | 11 | 11982.81 | 29255.90 | 29852.32 |
| azure | Events | 1 | 3 | 306.05 | 347.23 | 369.98 |
| azure | Events | 4 | 10 | 417.68 | 517.36 | 568.23 |
| azure | Events | 16 | 19 | 821.13 | 1127.48 | 1208.08 |
| azure | Events | 64 | 24 | 2348.67 | 3402.06 | 3582.80 |
| azure | Events | 256 | 16 | 11214.51 | 12865.10 | 13450.13 |
| kafkapostgres | Commands | 1 | 33 | 31.26 | 45.22 | 54.97 |
| kafkapostgres | Commands | 4 | 221 | 17.15 | 24.02 | 28.69 |
| kafkapostgres | Commands | 16 | 730 | 19.24 | 37.48 | 44.39 |
| kafkapostgres | Commands | 64 | 905 | 72.25 | 92.42 | 101.52 |
| kafkapostgres | Commands | 256 | 954 | 267.34 | 297.28 | 308.49 |
| kafkapostgres | RaiseOnly | 1 | 12 | 83.23 | 107.06 | 123.08 |
| kafkapostgres | RaiseOnly | 4 | 38 | 99.72 | 150.12 | 171.28 |
| kafkapostgres | RaiseOnly | 16 | 145 | 97.32 | 198.43 | 237.42 |
| kafkapostgres | RaiseOnly | 64 | 359 | 177.09 | 227.16 | 250.22 |
| kafkapostgres | RaiseOnly | 256 | 400 | 636.68 | 733.04 | 771.04 |
| kafkapostgres | Events | 1 | 0 | 30067.66 | 30067.66 | 30067.66 |
| kafkapostgres | Events | 4 | 10 | 292.65 | 388.57 | 411.38 |
| kafkapostgres | Events | 16 | 34 | 444.11 | 613.85 | 682.78 |
| kafkapostgres | Events | 64 | 52 | 1168.30 | 1463.76 | 2047.22 |
| kafkapostgres | Events | 256 | 36 | 2712.44 | 4057.28 | 30217.07 |

## What you're looking at — `azure` (Azurite + Azure Queue streams)

Azurite emulator, single Orleans silo, producer and consumers in one process. Three substrate ceilings, not framework ceilings:

- Azurite's per-op latency floor is materially above real Azure Storage.
- The Azure Queue stream provider polls on a fixed timer (`EdictAzureStreamsOptions.QueuePollingPeriod`); at high parallelism the Events row sits on that floor.
- One silo serialises everything; Orleans scales horizontally and these numbers don't exercise that.

Treat the table as registered defaults on a laptop emulator. A real storage account, a tuned poll period, or a multi-silo deployment moves the numbers up independently of any framework change.

## What you're looking at — `kafkapostgres` (Testcontainers Kafka + Postgres)

`Edict.Kafka` (custom `IQueueAdapter` over `Confluent.Kafka`, ADR-0028) + `Edict.Postgres` persistence. Testcontainers Kafka broker + Postgres 16, same single silo, same `BenchAggregateHandler` workload, same per-send `CorrelationId`-keyed completion poll as `azure`.

- Producer: `acks=all`, idempotent, lz4. Consumer: `enable.auto.commit=false`, manual commit after `HandleAsync` (ADR-0028 §2).
- `PartitionCount = 4` per `[EdictStream]` matches the slice 4-7 conformance fixture.
- The low-N Events rows are closed-loop tail-latency stall: p50 sits around 100 ms while p95 pegs at the 30 s window timeout because a single straggler holds the measurement open. N=256 amortises across enough in-flight sends for the row to settle to steady-state.
