# Edict.Benchmarks.Throughput

End-to-end throughput harness for the Edict pipeline. Per substrate, runs a closed-loop parallelism sweep + an open-loop saturation pass, then regenerates `docs/benchmarks/throughput.md` from a hand-curated template.

## Prerequisites

- .NET 10 SDK
- Docker Desktop (Testcontainers spins up Azurite for `azure`, Kafka + Postgres for `kafkapostgres`)

## Run

From the repo root:

```powershell
# Every registered substrate
dotnet run --project Edict/Edict.Benchmarks.Throughput -c Release -- all

# Single substrate
dotnet run --project Edict/Edict.Benchmarks.Throughput -c Release -- azure
dotnet run --project Edict/Edict.Benchmarks.Throughput -c Release -- kafkapostgres
```

Argument is required. Pass `all` or a substrate name from `SubstrateRegistry` (`azure`, `kafkapostgres`). Plan for ~14 min per substrate end-to-end (~30 min for `all`).

## What it measures

For each substrate, **two methodologies coexist** (see [ADR-0031](../../docs/adr/0031-throughput-two-methodologies.md)):

**Closed-loop sweep** — two scenarios swept across N ∈ {2, 16, 64} issuer tasks:

- **Command acceptance** — `IEdictSender.Send(...)` round-trip latency for `BenchIncrementCommand`.
- **Command → Event delivery** — `Send` + 5 ms point-get poll on the projection row.

Per scenario: 10 s warmup + 30 s measurement window. Produces p50/p95/p99 latency per (substrate, scenario, N).

**Saturation pass** — Events only, fixed N=256 producers fire-and-forget for 30 s after a 20 s warmup. Single sum-of-per-aggregate-counters read at window-end. One EPS row per substrate; no latency surface. The cluster is a fresh `TestCluster` separate from the closed-loop one — single-projection-per-cluster keeps the two measurements from contaminating each other.

## Output

- `docs/benchmarks/raw/<yyyy-MM-dd>-<substrate>-closedloop.csv` — per-point raw + downsampled latency samples (≤10k rows / point).
- `docs/benchmarks/raw/<yyyy-MM-dd>-<substrate>-saturation.csv` — one row: `(substrate, events_per_second, window_seconds, producer_concurrency, aggregate_count)`.
- `docs/benchmarks/throughput.md` — aggregate document, rewritten on every run from `docs/benchmarks/throughput.template.md` via a `{{token}}` replacer. The template is the only hand-curated surface; prose edits go there, never in the regenerated file.

Paths are resolved by walking up from the binary directory until `docs/` is found, so `dotnet run` works from any cwd.

## Adding a substrate

Implement `ISubstrate` + `ISubstrateRuntime` (in a sibling `Edict.Substrate.<Name>` library — see `Edict.Substrate.Azurite` and `Edict.Substrate.KafkaPostgres`), then add one line to `SubstrateRegistry.Registered`. A substrate that distinguishes saturation mode (e.g. `AutoOffsetReset = Latest` for Kafka) honours `SubstrateStartMode.Saturation` in its `StartAsync(...)`; substrates without a meaningful distinction (Azurite — Azure Queue streams have no offset-reset analogue) ignore the flag. No runner / writer changes.
