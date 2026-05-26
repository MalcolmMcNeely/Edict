# Edict throughput

Machine: Microsoft Windows 10.0.22631 / 20 cores
.NET version: 10.0.8
Run date: 2026-05-26
Git SHA: 3d7bb81

## Setup

- Substrate: Azurite (Testcontainers, default config) — not real Azure Storage.
- Single Orleans TestCluster silo (producer and consumers share one process).
- Azure Queue stream provider with framework defaults; queue polling sets a hard floor on per-event latency.
- Completion signal for the Events scenario is a 5 ms point-get poll against the projection table.
- Single run on dev hardware; expect ±20% variance run-to-run. Numbers are a baseline for the registered substrate, not a framework ceiling.

**azure: 660 commands/sec @ N=16**
**azure: 53 events/sec @ N=256**

| Substrate | Scenario | Parallelism | EPS | p50 (ms) | p95 (ms) | p99 (ms) |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| azure | Commands | 1 | 277 | 3.04 | 6.85 | 7.59 |
| azure | Commands | 4 | 553 | 7.30 | 10.16 | 11.89 |
| azure | Commands | 16 | 660 | 23.75 | 31.81 | 36.02 |
| azure | Commands | 64 | 568 | 112.42 | 127.96 | 136.40 |
| azure | Commands | 256 | 546 | 464.97 | 496.34 | 510.51 |
| azure | Events | 1 | 3 | 417.72 | 588.26 | 622.35 |
| azure | Events | 4 | 10 | 430.38 | 586.32 | 603.73 |
| azure | Events | 16 | 39 | 400.64 | 656.21 | 710.18 |
| azure | Events | 64 | 51 | 1201.23 | 1635.48 | 1793.47 |
| azure | Events | 256 | 53 | 4955.34 | 5520.62 | 7002.50 |
