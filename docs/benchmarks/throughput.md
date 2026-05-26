# Edict throughput

Machine: Microsoft Windows 10.0.22631 / 20 cores
.NET version: 10.0.8
Run date: 2026-05-26
Git SHA: 3f14fa8

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

**azure: 607 commands/sec @ N=16**
**azure: 85 raiseonly/sec @ N=64**
**azure: 64 events/sec @ N=64**

| Substrate | Scenario | Parallelism | Events per second (EPS) | p50 (ms) | p95 (ms) | p99 (ms) |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| azure | Commands | 1 | 304 | 2.99 | 4.87 | 6.09 |
| azure | Commands | 4 | 515 | 7.70 | 11.51 | 14.88 |
| azure | Commands | 16 | 607 | 25.21 | 36.40 | 52.13 |
| azure | Commands | 64 | 539 | 116.34 | 148.94 | 161.31 |
| azure | Commands | 256 | 516 | 493.42 | 530.33 | 541.55 |
| azure | RaiseOnly | 1 | 39 | 24.53 | 35.07 | 43.95 |
| azure | RaiseOnly | 4 | 79 | 48.04 | 78.90 | 105.71 |
| azure | RaiseOnly | 16 | 83 | 186.33 | 248.20 | 280.95 |
| azure | RaiseOnly | 64 | 85 | 741.87 | 818.11 | 850.80 |
| azure | RaiseOnly | 256 | 84 | 2988.44 | 3362.96 | 3428.52 |
| azure | Events | 1 | 8 | 123.66 | 186.32 | 201.54 |
| azure | Events | 4 | 29 | 138.09 | 195.47 | 216.61 |
| azure | Events | 16 | 59 | 266.90 | 360.79 | 402.96 |
| azure | Events | 64 | 64 | 991.32 | 1137.64 | 1207.28 |
| azure | Events | 256 | 62 | 4179.29 | 4496.81 | 6297.43 |
