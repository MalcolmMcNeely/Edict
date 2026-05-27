# Throughput: two methodologies in one document

Status: Accepted

## Context

`docs/benchmarks/throughput.md` is the artefact a stranger cloning Edict reads to answer "what throughput does this framework actually sustain?" One parallelism × scenario sweep cannot serve both readers of that document. The **headline reader** wants a single quotable sustained-EPS number per substrate, measured under a methodology defensible enough to put their name next to. The **engineering reader** wants the tail-latency surface and the Commands / RaiseOnly / Events budget decomposition across `N ∈ {1, 4, 16, 64, 256}`, because those are the numbers that say where time goes when a pipeline is slow. The pre-#144 file had only the second view — a thirty-row table, no top-of-doc number — and the reader had to know the methodology (closed-loop, paced per-issuer by `await`) and pick the peak themselves to extract anything resembling a headline. Closed-loop bounded-concurrency is the right shape for latency distributions and budget decomposition because each issuer's `await Send(...)` correctly serialises producer pressure to the consumer's rate; it is the wrong shape for a sustained-throughput claim because the EPS that falls out is correct-but-implicit — a first-time reader cannot tell whether a 66 EPS row reflects the producer hitting a ceiling or the consumer.

## Decision

Two methodologies coexist in the same document, generated from one benchmark invocation.

The **closed-loop sweep** is retained verbatim: N issuer tasks each `await sender.Send(...)` (Commands, RaiseOnly) or `await Send + WaitForEventRowAsync` (Events), 10 s warmup + 30 s window, latency histograms per issuer, p50/p95/p99 reported. The output is `{{table:closed_loop}}` — the parallelism × scenario × latency table that ships unchanged from the pre-#144 file.

The **saturation pass** is new: Events only, fixed N=256 producers fire-and-forget for 30 s after a 20 s warmup, a single sum-of-counters read at window-end, no per-event polling, no drain detection. The output is `{{table:saturation}}` — one row per substrate, one EPS column — landed at the top of the document as the unambiguous headline.

The saturation workload is **counter-per-aggregate** (`BenchCounterProjectionBuilder` ⇒ `benchcounter` table, one row per aggregate, increment-and-write on each event), not row-per-event. The property that earns the choice is fixed-cost end-of-window measurement: 1024 point-gets summed locally, regardless of how many events were processed. A row-per-event projection would write-amplify with EPS and contaminate either the measurement or the producer, depending on which side blocked first. Per-aggregate grain serialisation means the read-increment-write loop never contends on ETag.

The two methodologies run on **separate `TestCluster` instances** brought up in sequence per substrate: closed-loop first (registers `BenchProjectionBuilder` only), torn down, saturation second (registers `BenchCounterProjectionBuilder` only). Single-projection-per-cluster keeps the two measurements from cross-contaminating each other through doubled consumer-side write pressure.

Prose lives in `docs/benchmarks/throughput.template.md` and `MarkdownWriter` is a pure-function token replacer over `{{token}}` placeholders. The committed `throughput.md` is entirely regenerated; numbers cannot drift from the data and prose cannot be silently overwritten. The template is the only hand-curated surface.

## Honesty caveat

The saturation EPS on Events is not materially larger than the closed-loop Events peak — both find the same consumer ceiling. The saturation pass does not measure a different number; it measures the same number under a methodology that defends the claim. The win is **methodology defensibility** (open-loop sustained ceiling, not closed-loop peak picked out of a sweep) and **presentation** (single number at top-of-doc, framed by an operational sentence the reader can match against their own workload), not a larger headline. This was the grilling-session outcome on PRD #144 and is recorded here so a future reader of the document is not surprised that the saturation row and the closed-loop Events peak agree to within run-to-run variance.

## Trade-offs

- **No delivery cross-check.** The saturation pass reads a counter at window-end; it does not compare the count to the producer-issued send count `P`. A regression in Edict's at-least-once delivery would surface as lower EPS, indistinguishable from a real bottleneck. At-least-once is owned by the conformance suite, not this benchmark, so the loss of cross-check is acceptable here.
- **No saturation latency surface.** Per-event histograms, percentiles and samples are absent from the saturation pass. The closed-loop table remains the only latency surface; if a regression moves tail without moving steady-state EPS, the closed-loop sweep is what surfaces it.
- **Cluster bring-up doubled.** Two `TestCluster` instances per substrate per run pays the silo-startup cost twice. The 20 s saturation warmup overlaps that cost; total runtime impact is under ~15% of the existing closed-loop sweep.
- **Saturation fixed at N=256.** The saturation pass does not sweep parallelism. If the consumer ceiling is far below the producer's reach (Commands peak is 444 EPS Azure / 1481 EPS Kafka×Postgres, both above the Events consumer ceiling), N=256 outpaces consumer on both substrates today. A substrate where Events EPS approaches Commands EPS would need this revisited.

## Considered Options

- **Replace the closed-loop table with the saturation table.** Rejected: the latency surface and the budget decomposition (Commands / RaiseOnly / Events) are not subsumed by a single-substrate-single-number row. Engineering readers need both, and closing tail-latency regressions needs the existing histograms.
- **Sweep parallelism for the saturation pass.** Rejected: a sweep would reproduce the closed-loop sweep's shape and dilute the single-headline-number property that justifies the saturation methodology existing. Fixed N=256 is sufficient to outpace the consumer on both substrates today.
- **Apply saturation methodology to Commands and RaiseOnly as well.** Rejected: those scenarios are already producer-side-bound in the closed-loop sweep at high N (Commands hits 1336 EPS at N=256 on Kafka×Postgres while latency tail blows up — the producer is the limit), so saturation methodology would not produce meaningfully different numbers. Restricting the headline pass to Events keeps it focused on end-to-end consumer ceiling.
- **A drain-detection signal at window-end (wait for `count = P` before reading).** Rejected: drain detection turns an open-loop pass into a closed-loop pass that happens to start with a fire-and-forget burst, and the burst's queued backlog would be drained at the consumer's rate after the window — the reported EPS becomes (sum / time-to-drain), which is the same shape as closed-loop. The grilling session called this out as collapsing the methodology distinction.
- **Row-per-event saturation projection (`BenchProjectionBuilder`).** Rejected: write-amplifies with EPS, so the consumer-side write pressure climbs with the very throughput being measured, biasing the result low. Counter-per-aggregate is the fixed-cost shape end-of-window measurement requires.
- **Idempotent regions / preserved-hand-edit markers in `throughput.md`.** Rejected: the template is the only hand-curated surface, and `throughput.md` is entirely regenerated. Idempotent regions invite drift between the marker and the surrounding generated content; the regenerable-template design is the cleaner version.
- **`AutoOffsetReset = Earliest` for the saturation Kafka substrate.** Rejected: fresh-group consumers under `Earliest` would replay the warmup-window backlog into the measurement window, dragging the row down to the rate the consumer can chew through backlog rather than the rate it can absorb live events. `Latest` is the steady-state-throughput signal the saturation methodology needs; the Kafka substrate honours a `SubstrateStartMode.Saturation` flag through `ISubstrate.StartAsync(ct, mode)`.
- **Schema-registry-style versioning of the template.** Rejected: if the token vocabulary changes, the template is hand-edited at the same time. Template-format migration tooling is overkill for one document.

## Out of scope

- **Moving the actual ceiling.** Multi-silo `TestCluster`, real Azure Storage account behind a config flag, `QueuePollingPeriod` / `PartitionCount` tuning sweeps. These are where the headline number genuinely moves. This ADR covers the *methodology framing* and *presentation* of two views; it does not commit to any specific ceiling, and a future slice that introduces a real-cloud headline run will land its own ADR amendment or successor.
- **A "saturation"-style headline for non-throughput numbers.** Cold-start latency, recovery time after a substrate fault, dead-letter promotion latency — these have their own measurement contracts and will not be retro-fitted into the saturation table.
- **CONTEXT.md additions for "saturation" / "sustained EPS".** These stay benchmark-methodology terms, not domain terms. The glossary is for the framework's runtime concepts; the benchmark's vocabulary lives in its own doc.

## Cross-references

- ADR-0028 (`Edict.Kafka`) — owns the Kafka topology and the producer/consumer floors the saturation pass rides on. The `AutoOffsetReset = Latest` requirement flows through the substrate seam from this ADR to that one's per-mode wiring.
- ADR-0030 (substrate seam) — extended by a single `SubstrateStartMode` parameter on `ISubstrate.StartAsync(...)`; the seam shape from 0030 is unchanged in spirit.
- ADR-0027 (conformance harness) — owns the at-least-once and exactly-once-effect guarantees the saturation pass deliberately does not cross-check.
- PRD #144 — the user-story framing and grilling-session outcome ("the saturation EPS is not materially different from the closed-loop peak") that the Honesty caveat above documents.
