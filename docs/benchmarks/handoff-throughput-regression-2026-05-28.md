# Handoff — throughput regression triage (2026-05-28)

> **Resolved-portion note (2026-05-28, later same day):** the
> `Dispatch_…When1000CommandsFan` failure that an earlier session bundled
> into this handoff turned out to be a *separate* issue — Azurite
> first-time grain-blob/queue provisioning under the 32-parallel burst
> blew past Orleans' default 30 s response timeout. Fixed by bumping
> `SiloMessagingOptions.ResponseTimeout` + `ClientMessagingOptions.ResponseTimeout`
> to 2 minutes on `AzureClusterFixture`, mirroring the precedent in
> `EdictAzureSiloBuilderExtensionsClusterFixture` (same code comment,
> same root cause). Reproduces on `8611801` (the supposed "baseline good"
> SHA) too, so it was never a regression in the suspect window. The
> EPS-sweep portion of this doc below is still potentially valid — verify
> the EPS numbers with the clean-rerun gate before acting on it.

**Pick this up only if:** a clean re-run (no other Claude sessions, no orphan Docker containers) still shows azure < ~50 EPS or kafkapostgres < ~95 EPS at N=256 saturation. If the clean re-run snapped back to/above the 55 / 102 baseline, environmental contention was the cause and there's nothing to do here — discard this doc.

## Baseline vs. observed

- **Baseline (committed, SHA `8611801`)** — `azure = 55.20`, `kafkapostgres = 101.90` EPS. CSVs are at that SHA, same filenames as today's.
- **Today's working copy (HEAD `6986de1` + the 3 idle Claude sessions running)** — `azure = 38.73`, `kafkapostgres = 67.07` EPS. Numbers and full closed-loop tables live in:
  - `docs/benchmarks/raw/2026-05-28-azure-saturation.csv`
  - `docs/benchmarks/raw/2026-05-28-azure-closedloop.csv`
  - `docs/benchmarks/raw/2026-05-28-kafkapostgres-saturation.csv`
  - `docs/benchmarks/raw/2026-05-28-kafkapostgres-closedloop.csv`
  - `docs/benchmarks/throughput.md` (already regenerated with regressed numbers — **revert before pushing if cause turns out to be environmental**)

## What the original triage established

Distribution analysis (commands ran live in the previous session) showed a **uniform shift** of the full latency curve (p50 → max moved by the same multiplier) across **every** scenario/parallelism cell, including N=2 closed-loop where the system is nowhere near saturated. That signature is CPU/scheduler contention, **not** GC pauses, stream stalls, or lock contention spikes. If the clean re-run reproduces the regression, the uniform-shift signature is no longer the right explanation and you should re-bucket today's clean-run CSV against `git show 8611801:docs/benchmarks/raw/2026-05-28-*-closedloop.csv` before going further.

## Commits in the suspect window (`8611801..HEAD`)

Three perf PRs and one bench-runner change touched the hot path. Order of suspicion if a code regression is real:

1. **`b6764f5` — feat(perf): batch publish to same Domain Stream via OnNextBatchAsync (Slice D, #160)** — first suspect. New `ExecuteBatchAsync` seam, `OnNextBatchAsync` consumer path. A regression here would affect the `Command → Event delivery` and saturation rows more than `Command acceptance`. If `Command acceptance` is fine but delivery is the only thing regressed, this is almost certainly it.
2. **`8fe248f` — feat(perf): OutboxSlice.Pending over ImmutableList (Slice C, #159)** — hot allocation path change. Less likely but plausible if `Command acceptance` also regressed.
3. **`1002961` — feat(perf): generator-emitted EventStreamAccessors (Slice B, #158)** — replaces reflection at `AddEdict()` time; warmup-only cost, shouldn't show up after the 20 s warmup.
4. **`122df7a` — feat: UpsertRow Orleans codec + Serializer cache + DedupRingMirror (#157)** — affects projection write path; would primarily show up in `Command → Event delivery` and saturation.
5. **`75b91f4` — fix(bench): stop Throughput tests racing on ClusterHarness statics + clean up Kafka receivers on shutdown** — bench-runner fix, not framework. Cross-check: does this fix change which clusters get reused across the sweep, changing the warmup state?
6. **`bbba504` — feat(bench): surface producer-side failure rate** — added the `succeeded,failed,failure_rate,failure_types` CSV columns. Header-only addition, ignore.

PR titles map: #157→`122df7a`, #158→`1002961`, #159→`8fe248f`, #160→`b6764f5`.

## Bisect plan

If a clean re-run confirms the regression, bisect inside `8611801..HEAD` skipping the docs/test-only commits:

```
git bisect start HEAD 8611801
git bisect skip 6986de1 f0343bd 85f1100 f824799 e858e89 4b3a8c3 0dc8a24 31a14ea bbba504 88768b6
```

That leaves: `122df7a`, `1002961`, `8fe248f`, `b6764f5`, `75b91f4`. At each step, run only `azure` saturation (fastest signal — ~5 min) at N=256 from `Edict.Benchmarks.Throughput`. Anything materially above ~50 EPS at N=256 = good; below = bad. (Closed-loop sweep adds 30+ min per step and isn't needed for bisect.)

## Cross-substrate fingerprint to disambiguate

After identifying the regression range, run **both** substrates at N=2 closed-loop, `Command acceptance` only:
- Both regressed → framework hot path (Outbox, codecs, Orleans codegen).
- Only one regressed → substrate-specific code (`Edict.Kafka` or `Edict.Azure`).
- Neither regressed at N=2 but saturation tanks → producer batching / fan-out path (`#160` territory).

## Don't waste time on

- ADR-0028 / Kafka adapter options paths — they're load-bearing for `kafkapostgres` only. Azure regressing alongside rules them out.
- The Npgsql pool pressure work (#148, `88768b6`) — not in the suspect window.
- `MEMORY.md` entry "throughput-bench-flake-fixes" — fixes already in. If symptoms match flakes again, that's a separate issue.

## Skills to load next session

- **diagnose** — primary. Reproduce → minimise → hypothesise → fix → guard.
- **testing** — only if writing a regression guard for the bisect-discovered cause.
- **csharp** — if touching `Edict.Core` / `Edict.Azure` / `Edict.Kafka` source.

Skip the throughput tests by default rule (`MEMORY.md` → `dont-run-throughput-tests-by-default.md`) doesn't apply here — this session **is** throughput-specific.

## Working-tree state at handoff

`git status` had:
- `.claude/settings.local.json` — unrelated, leave or revert per preference.
- `docs/benchmarks/throughput.md` + the four `2026-05-28` CSVs — **modified by the regressed run**. Revert with `git checkout -- docs/benchmarks/throughput.md docs/benchmarks/raw/2026-05-28-*.csv` if the clean re-run vindicates the baseline. If the regression is real and bisect lands on a code commit, the *clean* run's CSVs are what should replace the baseline-committed ones.
