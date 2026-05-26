# Edict.Benchmarks.Throughput

End-to-end throughput harness for the Edict pipeline. Sweeps issuer parallelism against a pluggable `ISubstrate` and writes results to `docs/benchmarks/`.

## Prerequisites

- .NET 10 SDK
- Docker (Testcontainers spins up Azurite for the `azure` substrate)

## Run

From the repo root:

```powershell
# Single substrate
dotnet run --project Edict/Edict.Benchmarks.Throughput -c Release -- azure

# Every registered substrate
dotnet run --project Edict/Edict.Benchmarks.Throughput -c Release -- all
```

Argument is required. Pass `all` or a substrate name from `SubstrateRegistry`. Today the only registered substrate is `azure` (Azurite via Testcontainers).

## What it measures

For each substrate, two scenarios are swept across parallelism N ∈ {1, 4, 16, 64, 256}:

- **Commands** — `IEdictSender.Send` round-trip latency for `BenchIncrementCommand`.
- **Events** — command → projection row visible via `IEdictTableRepository` point-get (~5 ms poll).

Each point runs a 10 s warmup (discarded) followed by a 30 s measurement window. The substrate + TestCluster come up once per substrate and stay up across the sweep. Plan for ~7 minutes per substrate end-to-end.

## Output

- `docs/benchmarks/raw/<yyyy-MM-dd>-<substrate>.csv` — per-point raw + downsampled latency samples (≤10k rows / point).
- `docs/benchmarks/throughput.md` — aggregate table (EPS, p50/p95/p99), rewritten on every run.

Both paths are resolved by walking up from the binary directory until `docs/` is found, so `dotnet run` works from any cwd.

## Adding a substrate

Implement `ISubstrate` + `ISubstrateRuntime`, then add one line to `SubstrateRegistry.Registered`. No runner / writer changes (issue #126).
