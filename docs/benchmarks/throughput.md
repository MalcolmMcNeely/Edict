# Edict throughput

Machine: Microsoft Windows 10.0.22631 / 20 cores
.NET version: 10.0.8
Run date: 2026-05-26
Git SHA: 8e94351

## Setup

- Substrate: Azurite (Testcontainers, default config) — not real Azure Storage.
- Single Orleans TestCluster silo (producer and consumers share one process).
- Azure Queue stream provider with framework defaults; queue polling sets a hard floor on per-event latency.
- Completion signal for the Events scenario is a 5 ms point-get poll against the projection table.
- RaiseOnly measures Send latency with Raise in the handler; does not wait for the projection.
- Single run on dev hardware; expect ±20% variance run-to-run. Numbers are a baseline for the registered substrate, not a framework ceiling.

**azure: 653 commands/sec @ N=16**
**azure: 86 raiseonly/sec @ N=4**
**azure: 50 events/sec @ N=256**

| Substrate | Scenario | Parallelism | EPS | p50 (ms) | p95 (ms) | p99 (ms) |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| azure | Commands | 1 | 151 | 7.01 | 8.45 | 9.96 |
| azure | Commands | 4 | 590 | 6.31 | 10.60 | 12.47 |
| azure | Commands | 16 | 653 | 23.94 | 31.82 | 37.60 |
| azure | Commands | 64 | 572 | 111.94 | 126.71 | 134.36 |
| azure | Commands | 256 | 534 | 477.63 | 510.47 | 520.26 |
| azure | RaiseOnly | 1 | 44 | 22.25 | 36.15 | 45.61 |
| azure | RaiseOnly | 4 | 86 | 45.00 | 74.38 | 94.83 |
| azure | RaiseOnly | 16 | 81 | 189.50 | 267.23 | 337.83 |
| azure | RaiseOnly | 64 | 80 | 781.93 | 922.66 | 958.65 |
| azure | RaiseOnly | 256 | 78 | 3213.66 | 3505.24 | 3609.37 |
| azure | Events | 1 | 2 | 466.62 | 584.52 | 607.19 |
| azure | Events | 4 | 10 | 434.83 | 578.45 | 589.57 |
| azure | Events | 16 | 36 | 428.63 | 699.11 | 771.39 |
| azure | Events | 64 | 46 | 1359.87 | 1780.64 | 1945.39 |
| azure | Events | 256 | 50 | 5465.67 | 6167.31 | 8133.36 |
