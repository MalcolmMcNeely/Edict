# Edict throughput

Machine: Microsoft Windows 10.0.22631 / 20 cores
.NET version: 10.0.8
Run date: 2026-05-26
Git SHA: 0e754ff

## Setup

- Substrate: Azurite (Testcontainers, default config) — not real Azure Storage.
- Single Orleans TestCluster silo (producer and consumers share one process).
- Azure Queue stream provider with framework defaults; queue polling sets a hard floor on per-event latency.
- Completion signal for the Events scenario is a 5 ms point-get poll against the projection table.
- RaiseOnly measures Send latency with Raise in the handler; does not wait for the projection.
- Single run on dev hardware; expect ±20% variance run-to-run. Numbers are a baseline for the registered substrate, not a framework ceiling.

## What you're looking at

This is the framework on **Azurite** — a Node-based emulator running in a single container on dev hardware — with **one Orleans silo** hosting both producer and consumers. Three substrate ceilings are baked in before any Edict code runs:

1. Azurite's latency floor for blob and queue ops is materially higher than real Azure Storage.
2. The Azure Queue stream provider polls on a timer (`EdictAzureStreamsOptions.QueuePollingPeriod`). At high parallelism the Events row sits right on top of that floor — it is the substrate, not the silo.
3. A single silo serialises every producer and consumer onto one process; Orleans is designed to scale horizontally and these numbers do not exercise that.

Treat this table as **what the registered defaults give you on a laptop with the emulator**, not **what Edict can do**. A real Azure Storage account, a tuned poll period, and a multi-silo deployment all move these numbers up independently of any framework change.

**azure: 575 commands/sec @ N=64**
**azure: 74 raiseonly/sec @ N=16**
**azure: 60 events/sec @ N=256**

| Substrate | Scenario | Parallelism | Events per second (EPS) | p50 (ms) | p95 (ms) | p99 (ms) |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| azure | Commands | 1 | 228 | 3.44 | 8.06 | 9.74 |
| azure | Commands | 4 | 477 | 8.40 | 13.71 | 16.40 |
| azure | Commands | 16 | 574 | 27.25 | 37.03 | 43.43 |
| azure | Commands | 64 | 575 | 111.15 | 128.90 | 138.56 |
| azure | Commands | 256 | 529 | 475.23 | 538.44 | 604.37 |
| azure | RaiseOnly | 1 | 38 | 26.65 | 35.46 | 43.28 |
| azure | RaiseOnly | 4 | 69 | 56.05 | 75.80 | 98.97 |
| azure | RaiseOnly | 16 | 74 | 209.72 | 268.90 | 308.60 |
| azure | RaiseOnly | 64 | 73 | 850.66 | 989.83 | 1041.47 |
| azure | RaiseOnly | 256 | 70 | 3579.32 | 4213.73 | 5056.99 |
| azure | Events | 1 | 10 | 94.07 | 110.98 | 120.98 |
| azure | Events | 4 | 37 | 106.06 | 152.61 | 177.77 |
| azure | Events | 16 | 57 | 274.37 | 368.80 | 433.56 |
| azure | Events | 64 | 59 | 1075.49 | 1206.25 | 1272.98 |
| azure | Events | 256 | 60 | 4340.23 | 4649.69 | 6544.74 |
