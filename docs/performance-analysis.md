# Edict Performance Analysis (Framework-Only, Throughput Lens)

**Status:** Theorycraft / discussion input. Not a decision, not an ADR.
**Scope:** `Edict.Contracts`, `Edict.Core`, `Edict.Telemetry`. Substrate code (`Edict.Azure`, `Edict.Postgres`, `Edict.Kafka`) only enters when a framework choice forces a substrate cost. This is about whether *Edict itself* hits a CPU / allocation ceiling before the substrate would.
**Empirical grounding:** `docs/benchmarks/throughput.md` is the source of truth for what each substrate actually delivers on dev hardware. Items below that are graded High / Critical are graded against those measured numbers, not against an aspirational 5–10 k EPS that the bench has not yet observed on any registered substrate.

The framework's correctness story (atomic envelope, per-entry retry, dedup ring) has been load-bearing through several ADRs. Correctness is not the question here — **throughput-per-CPU-second and writes-per-command are**. This document inventories the hot paths, flags places where the *mechanism* costs more than the *work*, and grades each finding by how much it bites under sustained load.

The framework leans on three load-bearing primitives, all touched on every command/event:

1. **One atomic grain-document write** — `GrainEnvelope { Payload, Outbox, Idempotency }`.
2. **Outbox drain** — pure-transition slice, executor dispatch (now parallel via `Task.WhenAll`), single ack-batched write per pass.
3. **Per-consumer dedup ring** — bounded window of recently handled `EventId`s.

---

## 1. Scaling shape — does it scale?

Orleans's `(grainType, key)` placement spreads load by aggregate Guid across silos; per-grain turn discipline gives us atomic writes "for free"; implicit subscriptions mean a new consumer doesn't have to register out-of-band. The drain inside any one grain is now parallel (`OutboxHost.DrainAsync`, ADR 0015 amendment) so a Handle that raises N events publishes them concurrently rather than serially.

Three structural ceilings sit above the substrate, not below it:

| Ceiling | Where it bites | Operator surface |
|---|---|---|
| **Singleton consumer (fixed-Guid)** is a single grain — Orleans turn discipline serialises every event through it | An aggregate-fan-in projection, a fleet-wide audit log | Hard ceiling at ~one event-handler invocation per turn; today no warning |
| **Reminder service** is one cluster-wide table; lazy registration means steady-state is zero, but a fleet-wide downstream outage registers a reminder per dirty grain | 100 k aggregates wedged behind a slow downstream → 100 k reminder rows ticking at the 1-minute Orleans floor (`EdictOptions.OutboxDrainReminderPeriod`) | Tunable per ADR 0023 / EDICT options, but the Orleans floor of one minute is fixed |
| **Dead-letter projection** is a singleton table projection (one fixed-Guid grain) — every dead-letter promotion in the cluster lands on the same `EdictDeadLetterProjectionBuilder` grain | A bad deploy that dead-letters 1 % of 10 k EPS = 100 promotions/sec into one grain | No partitioning today; ADR 0022 explicitly chose a single global projection |

The framework also has a set of **per-event hot-path taxes** that scale linearly with EPS. Those are §3.

---

## 2. What's already strong (and what got fixed since the v1 of this doc)

### 2.1 Shipped optimisations that the v1 of this doc identified

| Item | Resolution | Reference |
|---|---|---|
| **N storage writes per N-event command** — was one `WriteStateAsync` per ack | Drain now coalesces every successful ack into one trailing write per pass via a `dirty` flag (`OutboxHost.cs:264`); failure paths (`FailWithBackoff` / `Promote`) keep inline writes for `AttemptCount` crash-monotonicity | #119 |
| **Double-serialise on the inline publish path** — was serialise (claim-check) → persist → deserialise (executor) → re-serialise into the stream frame | `ClaimCheckPolicy.ApplyAsync` now returns `(Payload, WireEvent)`; the inline drain hands the live ref straight to `PublishEventExecutor` which skips the deserialise (`OutboxHost.cs:151,212` + `PublishEventExecutor.cs:24`). Crash-recovery drains have no live ref and pay the deserialise as before | commit `c24bf30` |
| **Restart-from-head after every ack** — the v1 drain re-walked from index 0 on every success | Drain now snapshots `Pending.Where(p => p.NextAttemptUtc <= now)` once per pass and fires the executors concurrently via `Task.WhenAll`; outcomes apply serially on the grain task scheduler after the batch (`OutboxHost.cs:195–262`). ADR-0015 v1's rejection of parallel drain was reversed with bench evidence — the Commands → RaiseOnly cliff (`docs/benchmarks/throughput.md`) showed the queue PUT inside the executor dominated, not `WriteStateAsync` | #119, then commit `c24bf30` |
| **`WindowSize` DI lookup per event** in the dedup-ring hot path | Cached on first use per activation in `_cachedWindowSize` (`EdictIdempotencyBase.cs:68,80`) | commit `c24bf30` |
| **`OutboxDrainReminderPeriod` hardcoded** | Now a tunable on `EdictOptions` with a 1-minute floor (the Orleans reminder floor), validated at startup (`EdictOptions.cs:49`, `EdictOptionsValidator.cs:41`) | ADR 0023 |

### 2.2 Properties of the engine that are well-shaped for throughput

| Property | Mechanism | File / location |
|---|---|---|
| Common path is one atomic write at commit, then one trailing write per drain pass | `EnqueueAndDrainAsync` (`OutboxHost.cs:95`), `DrainAsync` (`OutboxHost.cs:174`) | one write to enqueue + one to ack-batch the pass, regardless of N |
| Drain fans out, outcomes fold in serially | `Task.WhenAll` over `ExecuteCapturingAsync` followed by sequential `Ack`/`Fail`/`Promote` on the grain task scheduler (`OutboxHost.cs:207–253`) | Slice stays a pure value, no cross-task contention |
| Steady-state reminder footprint is zero | Lazy register-when-dirty, unregister-on-drain (`OutboxHost.cs:293–309`) | A grain with nothing pending never touches the reminder subsystem |
| Backoff cannot stampede | Deterministic per-entry jitter from `EntryId` FNV-1a hash; no RNG, no clock (`OutboxBackoff.cs:53–85`) | Reproducible, no per-call randomness allocation |
| Claim-check is conditional, not universal | Small events ride the entry verbatim; only oversize pays the blob round-trip (`ClaimCheckPolicy.cs:58`) | Common case has zero blob I/O |
| Routing is a dict lookup, not reflection | `CommandRouteResolver` reads a generator-emitted `IReadOnlyDictionary<Type, CommandRoute>` | One ConcurrentDictionary lookup per command |
| Per-aggregate independence | ADR 0026: a poison entry no longer wedges the rest of its grain's pending | Throughput stays up under partial failure |

---

## 3. Concerns that still bite — graded by current code

### 3.1 Summary table

| # | Concern | Severity | Surface | Shape of the fix |
|---|---|---|---|---|
| 1 | **`OutboxSlice` mutations are O(n) each, drain is O(n²) over a pass** — `Ack`/`FailWithBackoff`/`Promote` each do `IndexOf` + a `[.. Pending.Take(i), …, .. Pending.Skip(i+1)]` list rebuild | Medium (was High; parallel drain shrinks typical pass-N) | `OutboxSlice.cs:35,55,82` | `ImmutableList<OutboxEntry>` (structural sharing, O(log n)) or a "tombstone + sweep on drain end" representation. Benchmark required — `EnqueueAndDrainAsync` already gets `EnqueueRange`'s O(n+m) shape via the per-entry foreach at `OutboxHost.cs:97`, so the win is concentrated in the drain-side Ack loop |
| 2 | **Reflection per event publish** — `EventStreamAddress.Resolve` re-reads `[EdictStream]` + scans properties via `GetCustomAttribute` / `GetProperties` / `PropertyInfo.GetValue` (boxed Guid back-out) on every call; `ClaimCheckPolicy.ApplyAsync` calls it three times in the oversize branch | Medium-High | `EventStreamAddress.cs:15–23`, `ClaimCheckPolicy.cs:76,77,82`, `PublishEventExecutor.cs:57` | Cache `(streamName, Func<EdictEvent,Guid>)` per `Type` in a `ConcurrentDictionary` keyed on first sight (compiled getter, no boxing) — or have the generator emit a static accessor map and skip reflection entirely |
| 3 | **`ServiceProvider.GetRequiredService<Serializer>()` per staged entry** in `EdictEventHandler`, `EdictSaga`, `EdictTableProjectionBuilder`, `EdictIdempotencyBase.StagePointerEnvelopeForDeferredDispatchAsync` | Low-Medium | `EdictEventHandler.cs:109`, `EdictSaga.cs:113`, `EdictTableProjectionBuilder.cs:129`, `EdictIdempotencyBase.cs:184` | Cache the resolved `Serializer` in a field on first use (same pattern as `_unwrap`); the serializer is a process-singleton |
| 4 | **Dedup-ring `Contains` is O(window size) per delivery** — `Array.IndexOf` linear scan | Low at default `WindowSize=100`; Medium-High for sized-up singletons | `EdictIdempotencyBase.cs:310–318` | Mirror the window with a `HashSet<Guid>` on activation; the array stays the canonical persisted state, the set is the in-memory accelerator. Pairs with `resiliency-analysis.md` §3.1 row 5 (sized-up singletons are the dangerous default) |
| 5 | **No batch publish to the same stream** — N events to the same `(streamName, routeKey)` from one command are N separate `IAsyncStream.OnNextAsync` calls (now concurrent via the parallel drain, so latency is hidden, but the per-message substrate cost is paid N times) | Medium under bursts on metered substrates | `PublishEventExecutor.cs:46` | Group entries by stream key in the drain and call the provider's batch overload where one exists. Parallel drain already collapsed the *latency* tax; this collapses the *cost* tax |
| 6 | **JSON-inside-Orleans for `UpsertRowEffect.RowJson`** — the row goes through `System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(row)`, then the `UpsertRowEffect` (containing those JSON bytes) is Orleans-serialised when the outbox entry is persisted; the executor inverts both | Medium | `EdictTableProjectionBuilder.cs:119`, `UpsertRowExecutor.cs:45` | The row already requires `[GenerateSerializer]` via `IEdictPersistedState` (EDICT011). Persist it as Orleans-serialised bytes (`serializer.SerializeToArray(row)` here, `serializer.Deserialize<T>(bytes)` at drain) — single codec, smaller payload, no second serialiser warmup |
| 7 | **`Activity.Current` + per-event span creation** at default sampling — every command opens a span, every publish opens a span, every handle opens a span | Operator-tunable; impact depends on exporter sizing | `EdictDiagnostics.ActivitySource`, every executor + base | Document sampling guidance at high EPS (recommend a `TraceIdRatioBasedSampler`); consider a fast-path no-span mode for `edict.event.claim_check.put`/`get` when the parent isn't recording (`ClaimCheckPolicy.cs:102`, `ClaimCheckUnwrap.cs:63`) |
| 8 | **`Guid.TraceId.ToHexString()` allocates a string per event** — called from every Outbox-staging seam (saga, event-handler, projection-builder, pointer-envelope intake) and again in the publisher executor | Low individually; visible in flame graphs at high EPS | `EdictSaga.cs:110`, `EdictEventHandler.cs:100`, `EdictTableProjectionBuilder.cs:126`, `EdictIdempotencyBase.cs:201`, `PublishEventExecutor.cs:41,42` | Cache the hex string on the Activity (or use a `Span<char>`-based stamp into a pooled `StringBuilder`) — only worth doing once the higher-severity items have ceiling room to spare |
| 9 | **Per-event `Guid.NewGuid()` per outbox entry** | Low | every entry construction | Cryptographic RNG cost; trivial in isolation, visible in profiles |
| 10 | **`new ValidationContext<TCommand>` per command** for FluentValidation | Low | `EdictCommandHandler.cs:152` | Inherent to FluentValidation; document if it becomes hot |

### 3.2 Why the severities shifted from the v1 doc

The v1 grading assumed the workload would sit at 5–10 k EPS. The throughput bench (`docs/benchmarks/throughput.md`) measures, on dev hardware with a single Orleans silo:

| Substrate | Commands peak EPS | RaiseOnly peak EPS | Events peak EPS |
|---|---:|---:|---:|
| `azure` (Azurite + Azure Queue streams) | 382 (N=64) | 32 (N=64) | 24 (N=64) |
| `kafkapostgres` (Kafka + Postgres) | 954 (N=256) | 400 (N=256) | 52 (N=64) |

Two things follow:

- The **Commands → RaiseOnly cliff** the v1 doc described was real and large; the v1 doc led to the parallel-drain + inline-ser-skip work (`c24bf30`) that closed it. RaiseOnly on `kafkapostgres` moved from the order-of-50 EPS captured in memory note `throughput-raiseonly-diff` to 400 EPS today.
- The **Events row ceiling** is dominated by consumer-side dispatch (the dedup ring → `Handle` → upsert chain), not the producer-side Outbox we have spent most cycles on. That means future severity for the §3.1 items above should be re-graded once a multi-silo bench is on the table — single-silo numbers under-weight everything that scales with `(grainType, key)`-partitioned placement.

### 3.3 Detail on the items above

#### §3.3.1 OutboxSlice O(n) per mutation

`OutboxSlice.cs:35,55,82` — every `Ack`/`FailWithBackoff`/`Promote` does `IndexOf` (`O(n)`) plus `[.. Pending.Take(i), .. Pending.Skip(i+1)]` (`O(n)` allocation). The parallel drain reduces this from "N successful entries → N consecutive O(n) Acks" to "N successful entries in *this pass* → N Acks", but the per-Ack list rebuild is still there.

A pending list of 20 entries draining successfully pays 20 + 19 + … + 1 = 210 list allocations per pass. At the current single-silo peak of ~400 RaiseOnly EPS on `kafkapostgres` this is invisible; at the multi-silo ceiling Edict is built for it is the next thing on the profile after `EventStreamAddress.Resolve`.

Two cheap moves:

| Approach | Cost change | Trade-off |
|---|---|---|
| `ImmutableList<OutboxEntry>` | O(log n) Ack | Slightly worse iteration constants; benchmark before committing |
| "Tombstone + sweep at end of pass" | O(1) Ack, O(n) sweep once per pass | Pending list briefly holds tombstones; needs care in the slice predicates |

#### §3.3.2 Reflection per event publish

`EventStreamAddress.cs:15–23`:

```csharp
var streamAttr = (EdictStreamAttribute?)Attribute.GetCustomAttribute(type, typeof(EdictStreamAttribute)) ?? throw …
var routeKeyProp = Array.Find(type.GetProperties(BindingFlags.Public | BindingFlags.Instance), p => Attribute.IsDefined(p, …)) ?? throw …
return (streamAttr.Name, (Guid)routeKeyProp.GetValue(evt)!);
```

`GetCustomAttribute`, `GetProperties`, `Array.Find`, `Attribute.IsDefined`, `PropertyInfo.GetValue` (Guid boxed back out of `object`) — **per event, per publish**. `ClaimCheckPolicy.cs:76,77,82` calls it three times in the oversize branch (inner stream name, inner route key, and the envelope-overflow throw path). The publisher executor calls it again at `PublishEventExecutor.cs:57`.

| Fix | Cost change | Effort |
|---|---|---|
| `ConcurrentDictionary<Type, (string Name, Func<EdictEvent,Guid> RouteKey)>` filled on first sight; route-key getter compiled via expression trees (no boxing) | One reflection hit per event type, then O(1) | Small |
| Generator-emitted `EventStreamAccessors` table — `Dictionary<Type, (string, Func<EdictEvent,Guid>)>` populated from `[EdictStream]` + `[EdictRouteKey]` at compile time, registered into DI | Zero reflection at runtime | Medium |

At today's measured peaks the reflective approach is survivable, but it is the kind of thing that turns into a fat band on the next flame graph for no reason.

#### §3.3.3 `Serializer` DI lookup per staged entry

The v1 doc grouped this with the dedup-ring DI lookup. Only the dedup-ring half is fixed (`c24bf30`, see §2.1). The four sites that still resolve `Serializer` per entry are `EdictEventHandler.cs:109`, `EdictSaga.cs:113`, `EdictTableProjectionBuilder.cs:129`, and `EdictIdempotencyBase.cs:184`. Same pattern: cache in a field on first use. Tiny patch, no design question, no consumer-visible surface.

#### §3.3.4 Dedup-ring `Contains` is O(window size)

`EdictIdempotencyBase.cs:310–318` — `Array.IndexOf` linear scan. At the default `WindowSize=100` this is invisible. At a sized-up singleton (`WindowSize=50_000` for a fleet-wide audit consumer) it is a per-event 50 k memory walk on the dispatch hot path, dominated by cache misses.

A `HashSet<Guid>` mirror rebuilt once on activation from the persisted ring and kept in sync as the ring rotates turns `Contains` into O(1). The ring stays the canonical persisted state (mirror is in-memory only), so no schema change is needed.

This pairs with `resiliency-analysis.md` §3.1 row 5 "dedup window is silently bounded" — both want operators to think about `WindowSize` for singletons, and the perf fix removes the disincentive to size it up.

#### §3.3.5 No batch publish to the same stream

`PublishEventExecutor.cs:46` — every entry's `stream.OnNextAsync(stamped)` is a single-message publish. A command that raises 3 events to the same `(streamName, routeKey)` pays 3 stream calls. Parallel drain runs them concurrently (the latency tax was the v1 concern and is gone), but each call still pays the substrate's per-message billable cost: 3 Azure Queue PUTs, 3 Kafka produces.

In the drain loop, group consecutive ready entries by `(streamName, routeKey)` and call the batch overload where the provider supports one. Grouping is local to one pass.

#### §3.3.6 JSON-inside-Orleans for `UpsertRowEffect.RowJson`

`EdictTableProjectionBuilder.cs:119` — the row is serialised with `System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(row)`, then the `UpsertRowEffect` (containing those JSON bytes) is Orleans-serialised at `EdictTableProjectionBuilder.cs:135` when the outbox entry is persisted. `UpsertRowExecutor.cs:45` inverts both at drain.

| Cost | Origin |
|---|---|
| Two serialisers warm in the same process | Tax on startup, GC churn on hot path |
| JSON bytes are 2–4× the Orleans binary equivalent | Bigger entry → bigger grain document → more substrate I/O per persist |
| `JsonSerializer.Deserialize(bytes, rowType)` boxing at drain | One reflection-driven path per row write |

The row already requires `[GenerateSerializer]` via `IEdictPersistedState` (EDICT011). Persist it as Orleans bytes directly. Same codec end-to-end, smaller payload, no second serialiser warmup.

---

## 4. Structural ceilings that still need design work

### 4.1 Singleton consumer is a single-thread bottleneck

A fixed-Guid singleton (the global escape hatch) is one grain → one turn at a time → one event-handler invocation at a time. If a consumer points an aggregating projection (e.g. "total orders today") at the singleton route, throughput is capped at one event per turn duration — call it ~5 k EPS on a fast handler, much less on a slow one. No analyzer warns; the failure mode is "throughput plateaus and nobody knows why". A diagnostic at activation that logs the singleton + EPS estimate would help.

### 4.2 Dead-letter projection is a singleton table projection

Every dead-letter promotion in the cluster lands on the same `EdictDeadLetterProjectionBuilder` grain (`EdictDeadLetterProjectionBuilder.cs:29–35`, ADR 0022 chose this explicitly). A bad deploy that dead-letters 1 % of 10 k EPS = 100 promotions/sec funnelling into one grain. The projection's Outbox sees 100 pending entries per second; the mechanism that *records* the storm is itself a candidate to fall behind during the storm.

Two shapes:
- **Partition by `EdictDeadLetterRaised.Kind` (or by source event type hash)** so high-volume dead-letter storms spread across grains; the singleton table partition stays `"deadletter"` so reads are unchanged.
- **Stay singleton but pre-allocate the Outbox slice and use the §3.3.5 batch-publish path** so the per-grain throughput grows enough to absorb a 1 %-of-fleet storm.

### 4.3 Implicit-subscription routing cost grows with handler count

Every `EdictEventHandler` subclass adds an `[ImplicitStreamSubscription]` rule. Orleans evaluates the rule set on every event delivery to decide which grains to activate. The generator already de-duplicates rules per stream (`EdictEventHandlerGenerator_ShouldDeduplicateSubscriptionAndEmitEveryHandleArm_WhenMultipleHandlesShareStream.verified.txt`), so two handlers on the same stream don't double the cost — but at 50+ event-handler types and a real multi-silo deployment, the per-delivery rule-evaluation cost is worth a microbenchmark before assuming it stays free.

### 4.4 Closure capture into dead-letter promotion

`OutboxHost.cs:242–243` — every promotion allocates an `EdictDeadLetterRaised` payload via `_promoter.Promote(bumped, exception, _grainKey, _grainTypeName, now)`. Each call carries `grainKey` and `grainTypeName` strings through the closure on the host instance (held for the host's lifetime — fine) but allocates the per-call envelope. Under a storm this is non-trivial; pool the promotion payload or let `IDeadLetterPromoter` accept a struct context to avoid the per-call allocation.

---

## 5. Bursts (5 k → 10 k spike)

The framework's burst behaviour is largely determined by Orleans's stream-provider backpressure, not by Edict's own code. Pulling agents smear the burst, dedup absorbs redelivery, lazy reminders keep steady state quiet.

The previous v1 doc identified **per-entry ack-writes** and **per-event serialise/deserialise round-trips** as the two amplification factors under burst. Both are closed (§2.1). What remains:

- **§3.3.1 OutboxSlice O(n²) drain** is the largest remaining burst amplifier. At pass-N = 20 entries it is 210 list allocations per pass; at pass-N = 100 entries (a real burst into one aggregate) it is 5 050.
- **§4.2 dead-letter projection singleton** is the burst-plus-poison failure mode: 1 % poison events into a single grain. The mechanism that records the storm should not be a single grain at that rate.

---

## 6. Headline summary and priority order

The framework is **correctness-first** by design and it shows in the code. The v1 of this doc identified six items as Critical / High; four are closed and one moved to Medium because the parallel drain shrank the typical N. The remaining work is mostly local fixes inside `Edict.Core` with no consumer-facing API change.

| Priority | Item | Why it tops the list |
|---|---|---|
| 1 | **Cache `EventStreamAddress.Resolve`** (§3.3.2) | Per-event reflection on the hot path; cheapest single-fix CPU win. `ConcurrentDictionary<Type, …>` or a generator-emitted table |
| 2 | **HashSet-mirror for dedup ring + sized singleton guidance** (§3.3.4) | Removes the perf disincentive to size singletons correctly; pairs with the resiliency-analysis dedup-window concern |
| 3 | **Cache `Serializer` field on every consumer base** (§3.3.3) | Tiny patch, free win, no design question |
| 4 | **Bench `ImmutableList<OutboxEntry>` vs current `List<>` for `OutboxSlice`** (§3.3.1) | The next thing on the profile after §3.3.2 once that's gone; benchmark-driven decision, not a guess |
| 5 | **Persist `UpsertRowEffect.RowJson` as Orleans bytes** (§3.3.6) | Drops one serialiser, halves the entry size |
| 6 | **Batch publish to the same stream** (§3.3.5) | Collapses the per-message substrate cost on metered substrates (Azure Queue, Kafka with cost-per-produce billing) |
| 7 | **Dead-letter projection partitioning** (§4.2) | The mechanism that records the storm should not itself be a single grain |
| 8 | **Operator docs: sampling guidance, singleton-consumer caveat, sized-up `WindowSize` recipe** (§3.1 #7, §4.1, §3.3.4) | No code change in the framework runtime; pure docs |

Every claim above is a working hypothesis until a benchmark proves it. `Edict.Benchmarks.Throughput` is the substrate-level bench; `Edict.Benchmarks` already has `OutboxDrainBenchmarks` for the in-memory drain — extend that suite before any of the §3 / §4 items ship, so the severity grading survives the optimisation.
