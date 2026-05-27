# Edict throughput

Machine: {{machine_class}}
.NET version: {{dotnet_version}}
Azure run date: {{run_date:azure}}
Kafka × Postgres run date: {{run_date:kafkapostgres}}
Git SHA: {{git_sha}}

## Sustained throughput

Open-loop Events workload: N=256 producers fire `Send(...)` as fast as they can for 30 s, after a 20 s warmup that lets JIT, grain caches, idempotency rings and the stream pulling agents reach steady state. The reported EPS is a single sum of per-aggregate counters read once at window-end, divided by 30 s — no per-event polling, no drain detection. Read this as the rate the substrate's consumer can absorb when the producer is not paced by the consumer; your own workload will only touch this ceiling if its per-event work is no heavier than the bench's counter increment.

{{table:saturation}}

## Setup

- Both substrates measured on the same machine and the same .NET runtime, one day apart, both registered through `Edict.Benchmarks.Throughput` via `SubstrateRegistry`.
- Single Orleans TestCluster silo per substrate run (producer and consumers share one process).
- Each scenario sweep is `N ∈ {1, 4, 16, 64, 256}` with 10 s warmup + 30 s measurement window.
- Completion signal for the Events scenario is a 5 ms point-get poll against the projection table (Azure Tables on Azurite, `benchevent` on Postgres).
- RaiseOnly measures Send latency with Raise in the handler; does not wait for the projection.
- Single run per substrate on dev hardware; expect ±20% variance run-to-run. Numbers are a baseline for the registered defaults of each substrate, not a framework ceiling.

{{table:closed_loop}}

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
