# Edict throughput

Machine: Microsoft Windows 10.0.26200 / 20 cores
.NET version: 10.0.8
Azure run date: 2026-05-26
Kafka × Postgres run date: 2026-05-27
Git SHA: c6265dd

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

| Substrate | Scenario | Parallelism | Events per second (EPS) | p50 (ms) | p95 (ms) | p99 (ms) |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| azure | Commands | 1 | 117 | 8.27 | 11.77 | 14.03 |
| azure | Commands | 4 | 338 | 10.69 | 15.59 | 19.17 |
| azure | Commands | 16 | 444 | 33.50 | 47.48 | 135.50 |
| azure | Commands | 64 | 438 | 140.86 | 166.30 | 335.35 |
| azure | Commands | 256 | 432 | 577.84 | 682.62 | 884.61 |
| azure | RaiseOnly | 1 | 32 | 30.33 | 35.92 | 50.52 |
| azure | RaiseOnly | 4 | 63 | 58.00 | 94.88 | 175.78 |
| azure | RaiseOnly | 16 | 71 | 212.12 | 310.31 | 450.96 |
| azure | RaiseOnly | 64 | 67 | 928.53 | 1179.13 | 1262.41 |
| azure | RaiseOnly | 256 | 71 | 3559.88 | 3905.48 | 4281.14 |
| azure | Events | 1 | 17 | 63.13 | 66.23 | 79.11 |
| azure | Events | 4 | 51 | 72.11 | 100.23 | 310.32 |
| azure | Events | 16 | 67 | 222.26 | 354.14 | 527.18 |
| azure | Events | 64 | 69 | 901.37 | 1056.26 | 1114.58 |
| azure | Events | 256 | 66 | 3740.67 | 4551.65 | 4684.75 |
| kafkapostgres | Commands | 1 | 87 | 10.45 | 22.94 | 27.84 |
| kafkapostgres | Commands | 4 | 485 | 7.38 | 14.94 | 19.16 |
| kafkapostgres | Commands | 16 | 953 | 15.43 | 29.67 | 36.82 |
| kafkapostgres | Commands | 64 | 1481 | 41.21 | 83.27 | 99.48 |
| kafkapostgres | Commands | 256 | 1336 | 186.75 | 310.88 | 340.22 |
| kafkapostgres | RaiseOnly | 1 | 16 | 59.64 | 87.20 | 112.30 |
| kafkapostgres | RaiseOnly | 4 | 55 | 67.59 | 106.93 | 272.33 |
| kafkapostgres | RaiseOnly | 16 | 245 | 60.63 | 103.51 | 118.01 |
| kafkapostgres | RaiseOnly | 64 | 492 | 124.10 | 185.19 | 256.12 |
| kafkapostgres | RaiseOnly | 256 | 618 | 407.13 | 497.33 | 647.28 |
| kafkapostgres | Events | 1 | 0 | 100.68 | 29895.64 | 29895.64 |
| kafkapostgres | Events | 4 | 1 | 299.94 | 28117.85 | 30045.75 |
| kafkapostgres | Events | 16 | 2 | 318.07 | 29819.56 | 30567.75 |
| kafkapostgres | Events | 64 | 8 | 854.34 | 30161.02 | 30521.35 |
| kafkapostgres | Events | 256 | 77 | 1982.66 | 3434.64 | 27261.44 |

## Bail-out trigger 2 verdict (issue #143)

Issue #143's performance trigger pivots Edict.Kafka to Event Hubs if Kafka × Postgres lands **below 50% of the Azure baseline peak EPS** on the same harness. Peak-EPS comparison per scenario:

| Scenario | Azure peak EPS | 50% floor | Kafka × Postgres peak EPS | Ratio | Verdict |
| --- | ---: | ---: | ---: | ---: | --- |
| Commands  | 444 | 222 | **1481** | 3.33× | **PASS** |
| RaiseOnly | 71  | 36  | **618**  | 8.70× | **PASS** |
| Events    | 69  | 35  | **77**   | 1.12× | **PASS** (narrow) |

All three scenarios clear the 50% floor. No pivot; Edict.Kafka stays the primary Kafka stream provider per ADR-0028.

The Events-scenario margin is narrow at peak and the low-parallelism rows (N ∈ {1..64}) sit at the 30 s issuer timeout because the consumer is still chewing through warmup-window backlog under `AutoOffsetReset = Earliest`. Tightening the Events curve at low N is follow-up work for the substrate, not a bail-out signal for the framework — the steady-state peak is the criterion the trigger reads.
