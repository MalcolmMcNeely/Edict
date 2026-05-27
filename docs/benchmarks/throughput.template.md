# Edict throughput

Machine: {{machine_class}}
.NET version: {{dotnet_version}}
Azure run date: {{run_date:azure}}
Kafka × Postgres run date: {{run_date:kafkapostgres}}
Git SHA: {{git_sha}}

## Sustained throughput

Fire-and-forget Events workload at N=256 producers, 20 s warmup + 30 s measurement window, single sum-of-counters read at window-end. EPS is the steady-state consumer ceiling for each substrate.

{{table:saturation}}

## Setup

- Both substrates measured on the same machine and the same .NET runtime, one day apart, both registered through `Edict.Benchmarks.Throughput` via `SubstrateRegistry`.
- Single Orleans TestCluster silo per substrate run (producer and consumers share one process).
- Each scenario sweep is `N ∈ {1, 4, 16, 64, 256}` with 10 s warmup + 30 s measurement window.
- Completion signal for the Events scenario is a 5 ms point-get poll against the projection table (Azure Tables on Azurite, `benchevent` on Postgres).
- RaiseOnly measures Send latency with Raise in the handler; does not wait for the projection.
- Single run per substrate on dev hardware; expect ±20% variance run-to-run. Numbers are a baseline for the registered defaults of each substrate, not a framework ceiling.

## What you're looking at — `azure` (Azurite + Azure Queue streams)

This is the framework on **Azurite** — a Node-based emulator running in a single container on dev hardware — with **one Orleans silo** hosting both producer and consumers. Three substrate ceilings are baked in before any Edict code runs:

1. Azurite's latency floor for blob and queue ops is materially higher than real Azure Storage.
2. The Azure Queue stream provider polls on a timer (`EdictAzureStreamsOptions.QueuePollingPeriod`). At high parallelism the Events row sits right on top of that floor — it is the substrate, not the silo.
3. A single silo serialises every producer and consumer onto one process; Orleans is designed to scale horizontally and these numbers do not exercise that.

Treat the `azure` table as **what the registered defaults give you on a laptop with the emulator**, not **what Edict can do**. A real Azure Storage account, a tuned poll period, and a multi-silo deployment all move these numbers up independently of any framework change.

## What you're looking at — `kafkapostgres` (Testcontainers Kafka + Postgres)

This is the framework on **`Edict.Kafka` (custom `IQueueAdapter` over `Confluent.Kafka`, ADR-0028) + `Edict.Postgres` persistence** — a Testcontainers Kafka broker + Postgres 16 instance on the same dev hardware as the `azure` run. Single silo, same `TestClusterBuilder` shape, same `BenchAggregateHandler` workload, same per-send `CorrelationId`-keyed completion poll. Differences vs `azure`:

- Producer rides `acks=all` + idempotent producer with lz4 compression; consumer rides `enable.auto.commit=false` with manual commit after `HandleAsync` (ADR-0028 §2).
- `PartitionCount = 4` per `[EdictStream]` matches `KafkaClusterFixture` so the substrate-run partition → key mapping is the same one the slice 4-7 conformance suites have proven.
- Substrate sets `AutoOffsetReset = Earliest` so fresh-group consumers replay from offset 0 — the warmup sweep produces backlog the measurement consumer chews through at low parallelism, which is the dominant reason for the very low Events-scenario EPS at N ∈ {1, 4, 16, 64} below. At N=256 the warmup backlog is amortised and the row settles to the substrate's actual steady-state.

## Headline peaks

**azure: 444 commands/sec @ N=16**
**azure: 71 raiseonly/sec @ N=16**
**azure: 69 events/sec @ N=64**

**kafkapostgres: 1481 commands/sec @ N=64**
**kafkapostgres: 618 raiseonly/sec @ N=256**
**kafkapostgres: 77 events/sec @ N=256**

{{table:closed_loop}}

## Bail-out trigger 2 verdict (issue #143)

Issue #143's performance trigger pivots Edict.Kafka to Event Hubs if Kafka × Postgres lands **below 50% of the Azure baseline peak EPS** on the same harness. Peak-EPS comparison per scenario:

| Scenario | Azure peak EPS | 50% floor | Kafka × Postgres peak EPS | Ratio | Verdict |
| --- | ---: | ---: | ---: | ---: | --- |
| Commands  | 444 | 222 | **1481** | 3.33× | **PASS** |
| RaiseOnly | 71  | 36  | **618**  | 8.70× | **PASS** |
| Events    | 69  | 35  | **77**   | 1.12× | **PASS** (narrow) |

All three scenarios clear the 50% floor. No pivot; Edict.Kafka stays the primary Kafka stream provider per ADR-0028.

The Events-scenario margin is narrow at peak and the low-parallelism rows (N ∈ {1..64}) sit at the 30 s issuer timeout because the consumer is still chewing through warmup-window backlog under `AutoOffsetReset = Earliest`. Tightening the Events curve at low N is follow-up work for the substrate, not a bail-out signal for the framework — the steady-state peak is the criterion the trigger reads.
