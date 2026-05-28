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
| **`Serializer` DI lookup per staged entry** in `EdictEventHandler` / `EdictSaga` / `EdictIdempotencyBase` / `EdictTableProjectionBuilder` | `Serializer? _cachedSerializer` field resolved via `??=` on first use per activation (`EdictEventHandler.cs:45`, `EdictSaga.cs:44`, `EdictIdempotencyBase.cs:71`, `EdictTableProjectionBuilder.cs:35`) | — |
| **Dedup-ring `Contains` was O(window size)** — `Array.IndexOf` linear scan on the per-event dispatch hot path | `DedupRingMirror` (`EdictIdempotencyBase.cs:69-70,316-337`) — in-memory `HashSet<Guid>` activated from the canonical persisted ring; `Contains` is O(1); the ring stays the durable state, the mirror is in-process only | — |
| **JSON-inside-Orleans for `UpsertRowEffect.RowJson`** — row serialised with `System.Text.Json` then the effect Orleans-serialised | `UpsertRowEffect.RowBytes` is now `byte[]` from the Orleans `Serializer` end-to-end; `RowAlias` carries the row type's frozen `[Alias]` so a class rename does not dead-letter (`UpsertRowEffect.cs:44-47`, `UpsertRowExecutor.cs:31`, `EdictTableProjectionBuilder.cs:113,123`) | — |
| **No batch publish to the same stream** — N events to the same `(streamName, routeKey)` paid the per-message substrate cost N times | Drain groups consecutive ready entries by stream key and calls `OnNextBatchAsync` where the provider supports it; the per-message Azure Queue PUT / Kafka produce cost collapses for fan-out commands | #160 |

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
| 1 | **Table-projection unconditional `GetAsync` per event** — every event handled by a per-aggregate table projection refetches the row from the substrate before invoking the handler, even though the grain is the single writer to that `(PK, RK)` by construction | **High** — binding constraint on the Events row of the throughput bench (52 EPS on `kafkapostgres`): each event pays one substrate round-trip *before* the work starts | `EdictTableProjectionBuilder.cs:85` | Cache the row in grain memory on first activation; subsequent events skip the GET. Outbox replays pending `UpsertRow` entries as full-row replaces, so crash recovery is unaffected |
| 2 | **Dead-letter projection is a singleton table projection** — every dead-letter promotion in the cluster lands on the same grain | High under storm (1% of 10k EPS → 100 promotions/sec into one grain); zero in steady state | `EdictDeadLetterProjectionBuilder.cs:29-31` | Partition by `EdictDeadLetterRaised.Kind` (or source-event-type hash); table partition stays `"deadletter"` so reads are unchanged. ADR-0022 chose singleton explicitly — supersession required |
| 3 | **Reflection per event publish via `EventStreamAccessorDiscovery`** — registrar discovery uses `MethodInfo.Invoke` to populate the runtime dictionary; `EventStreamAccessors.Resolve` is paid every publish | Medium-High | `EventStreamAccessorDiscovery.cs:36`, `EventStreamAccessors.cs:15-28`, `PublishEventExecutor.cs:54-57` | Generator-emitted accessor table populated at compile time, skipping `MethodInfo.Invoke`. Open as #158 |
| 4 | **`OutboxSlice` mutations are O(n) each, drain is O(n²) over a pass** — `Ack`/`FailWithBackoff`/`Promote` each do `IndexOf` + a `[.. Pending.Take(i), …, .. Pending.Skip(i+1)]` list rebuild | Medium (was High; parallel drain shrinks typical pass-N) | `OutboxSlice.cs:35,55,82` | `ImmutableList<OutboxEntry>` (structural sharing, O(log n)) or a "tombstone + sweep on drain end" representation. Open as #159; bundle the LINQ-in-drain-loop cleanup (`OutboxHost.cs:198-200,239`) into the same change |
| 5 | **Double serialise on `EdictEventHandler` deferred path** — `BuildInvokeHandlerEntry` does `SerializeToArray<EdictEvent>(evt)` → wrap in envelope → `SerializeToArray<EdictEvent>(envelope)`; drain inverts both | Medium on the Events row (one extra serialise + one extra deserialise per handled event, on the binding-constraint path) | `EdictEventHandler.cs:120,126` | Fast-path: when no claim-check key is present, persist raw event bytes and let the executor short-circuit the envelope unwrap |
| 6 | **`UpsertRowExecutor` deserialises via `Deserialize<object>`** — polymorphic codec path; the effect already carries `RowAlias` resolvable to a concrete `Type` | Low-Medium on projection-heavy workloads | `UpsertRowExecutor.cs:31` | Resolve `RowAlias` to `Type` once (cached per alias) and `Deserialize(rowType, bytes)`, skipping the polymorphism |
| 7 | **`Activity.Current` + per-event span creation** at default sampling — every command opens a span, every publish opens a span, every handle opens a span | Operator-tunable; impact depends on sampler and exporter sizing | `EdictDiagnostics.ActivitySource`, every executor + base | Document sampling guidance at high EPS (recommend a `TraceIdRatioBasedSampler`); consider a fast-path no-span mode for `edict.event.claim_check.put`/`get` when the parent isn't recording (`ClaimCheckPolicy.cs:102`, `ClaimCheckUnwrap.cs:63`) |
| 8 | **`Guid.TraceId.ToHexString()` allocates a string per event** — called from every Outbox-staging seam (saga, event-handler, projection-builder, pointer-envelope intake) and again in the publisher executor | Low individually; 2–3% cumulative across all sites; visible in flame graphs at high EPS | `EdictSaga.cs:110`, `EdictEventHandler.cs:100`, `EdictTableProjectionBuilder.cs:126`, `EdictIdempotencyBase.cs:201`, `PublishEventExecutor.cs:41,42` | Cache the hex string on the `Activity` (or use a `Span<char>`-based stamp into a pooled `StringBuilder`) |
| 9 | ~~**`evt.GetType().Name` allocates per Activity span open**~~ **Resolved as non-issue.** `Type.Name` on `RuntimeType` caches internally in modern .NET (`GetCachedName(TypeNameKind.Name)`) — `evt.GetType().Name` is a virtual call returning a cached string ref, not an allocation. A `ConcurrentDictionary<Type, string>` lookup is no cheaper than the two virtual calls it would replace. If the publish hot path ever needs to skip even those, the lever sits inside #158 (generator-emitted accessor table with const span names), not in a runtime dictionary | — | — | — |
| 10 | **Per-`Raise` `TimeProvider.GetUtcNow()` within one command turn** — every `Raise` reads the clock; for commands that raise N events that's N syscalls when one would do | Low; affects commands that raise >1 event | `EdictCommandHandler.cs:99` | Capture `now` once on command-handler entry and reuse across `Raise` calls in that turn |
| 11 | **Per-promote allocation in dead-letter path** — every promotion allocates an `EdictDeadLetterRaised` envelope through the host closure | Negligible in steady state; visible under poison storms (pairs with #2) | `OutboxHost.cs:242` | Pool the promotion payload, or have `IDeadLetterPromoter.Promote` accept a struct context |
| 12 | **Per-event `Guid.NewGuid()` per outbox entry** | Low | every entry construction | Cryptographic RNG cost; trivial in isolation, visible in profiles |
| 13 | **`new ValidationContext<TCommand>` per command** for FluentValidation | Low | `EdictCommandHandler.cs:152` | Inherent to FluentValidation; document if it becomes hot |

### 3.2 Why the severities shifted

The v1 grading assumed the workload would sit at 5–10 k EPS. The throughput bench (`docs/benchmarks/throughput.md`) measures, on dev hardware with a single Orleans silo:

| Substrate | Commands peak EPS | RaiseOnly peak EPS | Events peak EPS |
|---|---:|---:|---:|
| `azure` (Azurite + Azure Queue streams) | 382 (N=64) | 32 (N=64) | 24 (N=64) |
| `kafkapostgres` (Kafka + Postgres) | 954 (N=256) | 400 (N=256) | 52 (N=64) |

Three things follow:

- The **Commands → RaiseOnly cliff** the v1 doc described was real and large; the v1 doc led to the parallel-drain + inline-ser-skip work (`c24bf30`) that closed it. RaiseOnly on `kafkapostgres` moved from the order-of-50 EPS captured in memory note `throughput-raiseonly-diff` to 400 EPS today.
- The **RaiseOnly → Events cliff** (400 → 52 EPS on `kafkapostgres`) is the binding constraint for projection-heavy workloads. The consumer-side dispatch chain (dedup ring → `Handle` → upsert) dominates, and the perf doc's previous claim that the dedup ring was the suspect is now ruled out: §2.1 closed the `Serializer` DI lookup, the dedup-ring linear scan, and the JSON-inside-Orleans row encoding. What remains on the consumer side — and what the Orleans-alignment review surfaced — is concern §3.1 #1: every event in a per-aggregate table projection eats a substrate round-trip *before* the work starts. That makes #1 the highest-leverage open item by a wide margin.
- The **per-message stream cost** that was §3.3.5 is now closed by #160 (`OnNextBatchAsync`). Burst behaviour for fan-out commands is no longer rate-limited by the per-PUT / per-produce overhead.

### 3.3 Detail on the higher-leverage items

#### §3.3.1 Table-projection unconditional `GetAsync` per event (item #1)

`EdictTableProjectionBuilder.cs:85`:

```csharp
CurrentRow = await _writeStore!.GetAsync(partitionKey, rowKey) ?? new T();
await handler(evt);
_pendingUpsert = BuildUpsertEntry(partitionKey, rowKey, CurrentRow);
```

Every event handled by a per-aggregate table projection refetches the row from the substrate before invoking the consumer's handler. For an Azure Tables read the cost is a Tables RPC; for Postgres it's a SELECT round-trip. On the Events row of the throughput bench — currently 52 EPS on `kafkapostgres` — this is one full substrate hop per event *before any work happens*, and the work it gates is itself I/O-bound by the `UpsertRow` effect.

The grain is the **single writer** to its `(PartitionKey, RowKey)` by construction: Orleans routes by `(grainType, primaryKey)`, the projection's primary key equals the row's partition key, and no other grain instance can write to the same row. So after the first activation, the row's in-memory copy *is* authoritative — the only reason to re-read is to handle stale-after-crash, and crash recovery is handled by replaying the outbox's pending `UpsertRow` entries (which are full-row replaces, idempotent by pk/rk).

The fix:

| Step | Cost change |
|---|---|
| First event per activation: load from store (cold) | Same as today — one round-trip |
| Subsequent events: skip the GET, use the in-memory `CurrentRow` | Drops one round-trip per event |
| Re-load on deactivation/reactivation if Orleans evicts the grain | Same as today — Orleans handles this |
| Crash recovery: outbox replays pending `UpsertRow` full-row replaces | Unchanged — the row converges |

At the current Events ceiling this is plausibly a 2–3× win (52 EPS → 100–150 EPS on `kafkapostgres`), and the invariant the optimisation depends on (single-writer-per-row) is already true in the design. The change is local to `EdictTableProjectionBuilder`.

#### §3.3.2 Dead-letter projection partitioning (item #2)

`EdictDeadLetterProjectionBuilder.cs:29-31`. Every dead-letter promotion in the cluster lands on the same singleton grain. ADR-0022 chose this explicitly — fleet-wide reads are simpler when every row lives in one table partition — but the *write* side of that choice is a single-grain bottleneck under failure-mode storms: a bad deploy that dead-letters 1% of 10 k EPS = 100 promotions/sec into one grain. The grain's own Outbox grows pending entries faster than the drain can ship them; the mechanism that *records* the storm becomes a candidate to fall behind during the storm.

Two shapes (the first is the obvious one):

| Approach | Read change | Write change | Compatibility |
|---|---|---|---|
| Partition by `EdictDeadLetterRaised.Kind` (or `SourceEventType` hash) into N grain instances; table partition stays `"deadletter"` | Unchanged — fleet-wide reads still hit one table partition | Spreads writes across N grains | ADR-0022 supersession; existing dead-letter table layout preserved |
| Stay singleton; rely on the §2.1 batch-publish path (#160) to give the singleton enough per-grain throughput to absorb a 1%-of-fleet storm | Unchanged | Per-grain throughput grows; but per-grain turn discipline still caps | Cheaper change; less headroom |

The first option has more headroom; ADR-0022 supersession is the gating decision.

#### §3.3.3 Reflection per event publish (item #3, open as #158)

`EventStreamAccessorDiscovery.cs:36` populates the runtime accessor dictionary via `MethodInfo.Invoke` against an assembly-emitted `EdictRouteRegistrar` (ADR-0017). At publish time, `EventStreamAccessors.Resolve` (`EventStreamAccessors.cs:15-28`) does a dictionary lookup keyed on `evt.GetType()` — already O(1), no reflection per publish. The remaining reflection cost is concentrated at startup discovery and at any code path that walks the registrar contributions.

#158's framing is to have the generator emit the accessor table as a static `Dictionary<Type, EdictEventStreamAccessor>` populated from `[EdictStream]` + `[EdictRouteKey]` at compile time, with the route-key getter compiled (no `PropertyInfo.GetValue` Guid boxing), and the discovery seam reduced to a straight assembly scan with no `MethodInfo.Invoke`. Zero reflection at runtime.

At today's measured peaks the current shape is survivable, but it's the kind of thing that turns into a fat band on the next flame graph for no reason.

#### §3.3.4 OutboxSlice O(n) per mutation (item #4, open as #159)

`OutboxSlice.cs:35,55,82` — every `Ack`/`FailWithBackoff`/`Promote` does `IndexOf` (`O(n)`) plus `[.. Pending.Take(i), .. Pending.Skip(i+1)]` (`O(n)` allocation). The parallel drain reduces this from "N successful entries → N consecutive O(n) Acks" to "N successful entries in *this pass* → N Acks", but the per-Ack list rebuild is still there.

A pending list of 20 entries draining successfully pays 20 + 19 + … + 1 = 210 list allocations per pass. At the current single-silo peak of ~400 RaiseOnly EPS on `kafkapostgres` this is invisible; at the multi-silo ceiling Edict is built for, it surfaces.

| Approach | Cost change | Trade-off |
|---|---|---|
| `ImmutableList<OutboxEntry>` | O(log n) Ack | Slightly worse iteration constants; benchmark before committing |
| "Tombstone + sweep at end of pass" | O(1) Ack, O(n) sweep once per pass | Pending list briefly holds tombstones; needs care in the slice predicates |

Bundle the LINQ-in-drain-loop cleanup at `OutboxHost.cs:198-200,239` into the same change — `Pending.Where(...).ToArray()` per pass and `Pending.FirstOrDefault(...)` after each failure both go away in a hand-rolled `for` loop, and the new slice representation has to provide a non-LINQ iteration surface anyway.

#### §3.3.5 Double serialise on `EdictEventHandler` deferred path (item #5)

`EdictEventHandler.cs:120,126`:

```csharp
var envelope = evt is EdictEventEnvelope already
    ? already
    : EnvelopeCodec.WrapInline(serializer.SerializeToArray<EdictEvent>(evt));
// …
Payload = serializer.SerializeToArray<EdictEvent>(envelope),
```

The inner `SerializeToArray<EdictEvent>(evt)` produces bytes-A; those bytes get wrapped in an `EdictEventEnvelope`; the envelope is then `SerializeToArray<EdictEvent>(envelope)` to produce bytes-B (the durable payload). At drain time, the executor deserialises bytes-B → unwraps to bytes-A → deserialises bytes-A → dispatches. **Two serialises + two deserialises per event handled.**

The inline-envelope wrap exists so the executor can use one `ClaimCheckUnwrap` codepath. But for the not-claim-check case (which is the common case — claim-check only fires for oversize events past `ClaimCheckThresholdBytes`) the inner serialise is redundant: the executor could fast-path on "envelope-wraps-inline-bytes" and the entry could store the raw event bytes directly.

The Command-handler inline-publish path already pays only one serialise via the live-ref hand-off (`OutboxHost.cs:36` and the comment at `PublishEventExecutor.cs:21-24`). The Event-handler path is the asymmetric one.

This concern is concentrated on the Events row of the throughput bench (the binding constraint) — every event handled by an `EdictEventHandler` pays the extra serialise + deserialise.

---

## 4. Structural ceilings that still need design work

These are concerns whose severity isn't known well enough to grade. They sit above the §3 fix-list because the answer is "benchmark, then decide".

### 4.1 Singleton consumer is a single-thread bottleneck

A fixed-Guid singleton (the global escape hatch) is one grain → one turn at a time → one event-handler invocation at a time. If a consumer points an aggregating projection (e.g. "total orders today") at the singleton route, throughput is capped at one event per turn duration — call it ~5 k EPS on a fast handler, much less on a slow one. No analyzer warns; the failure mode is "throughput plateaus and nobody knows why". A diagnostic at activation that logs the singleton + EPS estimate would help.

### 4.2 Implicit-subscription routing cost grows with handler count

Every `EdictEventHandler` subclass adds an `[ImplicitStreamSubscription]` rule. Orleans evaluates the rule set on every event delivery to decide which grains to activate. The generator already de-duplicates rules per stream (`EdictEventHandlerGenerator_ShouldDeduplicateSubscriptionAndEmitEveryHandleArm_WhenMultipleHandlesShareStream.verified.txt`), so two handlers on the same stream don't double the cost — but at 50+ event-handler types and a real multi-silo deployment, the per-delivery rule-evaluation cost is worth a microbenchmark before assuming it stays free.

---

## 5. Bursts (5 k → 10 k spike)

The framework's burst behaviour is largely determined by Orleans's stream-provider backpressure, not by Edict's own code. Pulling agents smear the burst, dedup absorbs redelivery, lazy reminders keep steady state quiet.

The previous v1 doc identified **per-entry ack-writes**, **per-event serialise/deserialise round-trips**, and **per-message stream cost** as the three amplification factors under burst. All three are closed (§2.1, including #160 for the per-message stream cost). What remains:

- **§3.3.4 OutboxSlice O(n²) drain** is the largest remaining burst amplifier (open as #159). At pass-N = 20 entries it is 210 list allocations per pass; at pass-N = 100 entries (a real burst into one aggregate) it is 5 050.
- **§3.3.2 dead-letter projection singleton** is the burst-plus-poison failure mode: 1 % poison events into a single grain. The mechanism that records the storm should not be a single grain at that rate. Item #2 in the §3.1 table; needs ADR-0022 supersession to land.
- **§3.3.1 table-projection unconditional `GetAsync`** is not a burst amplifier per se, but it's the binding constraint that determines what the burst floor *looks like* on the Events row — any consumer-side burst into a per-aggregate projection is currently paying one substrate round-trip per event before work starts.

---

## 6. Headline summary and priority order — ranked by estimated gain

The framework is **correctness-first** by design and it shows in the code. The v1 of this doc identified six items as Critical / High; **all six are now closed** (`c24bf30` parallel drain + inline ser skip, `c24bf30` cached `Serializer` and dedup HashSet, `UpsertRowEffect` Orleans bytes, #160 batch publish via `OnNextBatchAsync`). The remaining work is mostly local fixes inside `Edict.Core` with no consumer-facing API change.

Estimates without a profiler are *estimates*. Anchor for "estimated gain" is the throughput bench's current ceilings (`docs/benchmarks/throughput.md`): Commands ~954 EPS, RaiseOnly ~400 EPS, Events ~52 EPS on `kafkapostgres`; the **Events row is the binding constraint** for any consumer-side projection workload. Every row below is a working hypothesis until `Edict.Benchmarks.OutboxDrainBenchmarks` and `Edict.Benchmarks.Throughput` confirm it.

| Rank | Item | Est. gain | Where it bites | Status | Effort |
| ---: | --- | --- | --- | --- | --- |
| 1 | **§3.3.1 — Table-projection row cache across events** | **Largest single lever.** Plausibly 2–3× the Events row (52 EPS → 100–150 EPS on `kafkapostgres`). Eliminates one substrate round-trip per event on the binding-constraint path | Per-aggregate table projections at sustained EPS | Not filed | Medium — invariant (grain is single writer to its row) is already true; just stop fetching |
| 2 | **§3.3.2 — Dead-letter projection partitioning** | Order-of-magnitude under poison storms (projection stops being the bottleneck); zero in steady state | Bad deploys, downstream outages | Not filed (ADR-0022 supersession) | Medium |
| 3 | **§3.3.3 — Generator-emitted `EventStreamAccessors` map** | 5–15% on the publish hot path. Reflection at the discovery seam closes for good | Every publish path | Open as #158 | Medium |
| 4 | **§3.3.4 — `OutboxSlice` over `ImmutableList`** | 5–10% under burst (pass-N ≥ 20 entries); negligible at typical pass-N. Was Critical in the v1 doc, downgraded to Medium after parallel drain | Burst-time amplifier; aggregates that raise many events per command | Open as #159 | Small-Medium |
| 5 | **§3.3.5 — Skip redundant inner serialise on `EdictEventHandler` deferred path** | ~3–8% on the Events row. One serialise + one deserialise per event handled, on the binding-constraint path | Every `EdictEventHandler.Handle` invocation | Not filed | Small |
| 6 | **§3.1 #6 — Typed `Deserialize(rowType, bytes)` in `UpsertRowExecutor`** | 1–3% per upsert. `RowAlias` already resolves to a concrete `Type`; cache per alias and skip the polymorphic codec | Projection-heavy workloads at drain | Not filed | Small |
| 7 | **§3.1 #8 — Cache `TraceId.ToHexString()` allocations** | <1% individually; 2–3% cumulative across all the staging seams. Flame-graph fat band rather than a measurable throughput cliff | Every outbox-staging seam | Not filed | Small |
| ~~8~~ | ~~**§3.1 #9 — Cached `Type → Name` for span tagging**~~ | **Resolved as non-issue** — `Type.Name` is internally cached in modern .NET; the proposed `ConcurrentDictionary<Type, string>` is no cheaper than the virtual calls it replaces. If the publish hot path ever needs to skip even those, fold into #158 (generator-emitted const span names) | — | Skipped | — |
| 9 | **§3.1 #11 — Struct/pooled context on `IDeadLetterPromoter.Promote`** | Negligible in steady state; visible only under poison storms (pairs with rank 2) | Storm scenarios | Not filed | Small |
| 10 | **§3.1 #10 — Capture `now` once per command turn on `Raise`** | <1%. One `TimeProvider.GetUtcNow()` per `Raise` call within a turn → once per turn | Commands that raise multiple events | Not filed | Trivial |
| 11 | **§3.1 #12 — `Guid.NewGuid()` per outbox entry** | Negligible; pure CPU cost from the cryptographic RNG path | Every outbox entry construction | Not filed | Trivial (defer until profiled) |
| 12 | **§3.1 #13 — `ValidationContext<TCommand>` per command** | Negligible; inherent to FluentValidation | Every command with a validator | Not filed | Out-of-framework |
| — | **§3.1 #7 / §4.1 — Operator docs: sampling guidance, singleton-consumer caveat, sized-up `WindowSize` recipe** | No runtime gain; prevents misconfigurations that silently *cap* performance | Misconfigured production deployments | Not filed | Docs only |
| — | **§4 structural items** (singleton consumer warning, implicit-subscription rule cost) | Unknown until microbenchmarked | Multi-silo deployments at scale | Benchmark first | Investigation |

If only one thing ships next, ship rank 1 (the table-projection row cache). It targets the binding constraint (the Events row), the invariant the optimisation depends on is already true in the design, and the change is local to `EdictTableProjectionBuilder`. Everything else either improves a non-binding row (rank 3, 4, 7, 8, 10), only matters under specific failure modes (rank 2, 9), or chips at a percentage that the Events ceiling will dominate until rank 1 is in.

`Edict.Benchmarks.Throughput` is the substrate-level bench; `Edict.Benchmarks` already has `OutboxDrainBenchmarks` for the in-memory drain — extend that suite before any of the §3 / §4 items ship, so the severity grading survives the optimisation.
