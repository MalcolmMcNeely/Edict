# Edict throughput

Machine: Microsoft Windows 10.0.22631 / 20 cores
.NET version: 10.0.8
Run date: 2026-05-26
Git SHA: a0ade30

**azure: 649 commands/sec @ N=16**
**azure: 93 events/sec @ N=64**

| Substrate | Scenario | Parallelism | EPS | p50 (ms) | p95 (ms) | p99 (ms) |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| azure | Commands | 1 | 324 | 2.83 | 4.42 | 5.81 |
| azure | Commands | 4 | 498 | 7.88 | 11.35 | 13.31 |
| azure | Commands | 16 | 649 | 24.04 | 32.58 | 36.85 |
| azure | Commands | 64 | 563 | 113.05 | 129.28 | 136.85 |
| azure | Commands | 256 | 547 | 464.95 | 496.24 | 506.05 |
| azure | Events | 1 | 3 | 313.07 | 540.38 | 572.15 |
| azure | Events | 4 | 17 | 201.31 | 552.91 | 595.45 |
| azure | Events | 16 | 85 | 157.79 | 486.15 | 642.09 |
| azure | Events | 64 | 93 | 670.43 | 840.42 | 1129.90 |
| azure | Events | 256 | 80 | 3122.05 | 3505.33 | 3792.34 |
