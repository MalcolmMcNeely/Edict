# Edict Performance Analysis (Framework-Only, Throughput Lens)

**Status:** Theorycraft / discussion input. Not a decision, not an ADR.
**Scope:** `Edict.Contracts`, `Edict.Core`, `Edict.Telemetry`, with notes where `Edict.Azure` implementations matter. Assumes the consumer's substrate (Azure Tables/Blobs/Queues, future DynamoDB, etc.) is sized for the workload — this document is about whether *Edict itself* exacerbates the storage bill or hits a CPU/allocation ceiling before the substrate would.
**Workload assumed:** sustained ~5 000 commands/events per second across an AKS cluster, bursts to ~10 000, many silos, healthy grain-key distribution (no single hot aggregate).

The framework's correctness story (atomic envelope, per-entry retry, dedup ring) has been load-bearing through several ADRs. Correctness is not the question here — **throughput-per-CPU-second and writes-per-command are**. This document inventories the hot paths, flags places where the *mechanism* costs more than the *work*, and grades each finding by how much it bites at burst.

The framework leans on three load-bearing primitives, all touched on every command/event:

1. **One atomic grain-document write** — `GrainEnvelope { Payload, Outbox, Idempotency }`.
2. **Outbox drain** — pure-transition slice, executor dispatch, post-success ack write.
3. **Per-consumer dedup ring** — bounded window of recently handled `EventId`s.

---

## 1. Scaling shape — does it scale?

The geometry is favourable. Orleans's `(grainType, key)` placement spreads load by aggregate Guid across silos; per-grain turn discipline gives us atomic writes "for free"; implicit subscriptions mean a new consumer doesn't have to register out-of-band. **Provided the consumer's aggregate keyspace is wide enough that no single grain becomes a hot spot**, throughput scales horizontally with silo count and the underlying stream provider's pulling-agent count. 5 k EPS across, say, 10 silos with 5 pulling agents apiece is 100 EPS per agent — comfortable.

Three structural ceilings sit above the substrate, not below it:

| Ceiling | Where it bites | Operator surface |
|---|---|---|
| **Singleton consumer (fixed-Guid)** is a single grain — Orleans turn discipline serialises every event through it | An aggregate-fan-in projection, a fleet-wide audit log | Hard ceiling at ~one event-handler invocation per turn; today no warning |
| **Reminder service** is one cluster-wide table; lazy registration means steady-state is zero, but a fleet-wide downstream outage registers a reminder per dirty grain | 100 k aggregates wedged behind a slow downstream → 100 k reminder rows churning at 1-minute period | Documented in `resiliency-analysis.md` §4; the 1-minute floor is hardcoded |
| **Dead-letter projection** is a singleton table projection (one fixed-Guid grain) — under a poison-event storm every dead-letter promotion lands in the same grain | A bad deploy that dead-letters 1 % of 10 k EPS = 100 promotions/sec into one grain | No partitioning today; ADR 0022 explicitly chose a single global projection |

The framework also has a set of **per-event hot-path taxes** that scale linearly with EPS and that current implementations make worse than they need to be. Those are the meat of §3.

---

## 2. What's genuinely well-shaped for throughput

Worth naming so the perf review doesn't read as one-sided.

| Property | Mechanism | File / location |
|---|---|---|
| Common path is one write, not two-phase | `EnqueueAndDrainAsync` commits `{Payload, Outbox, Idempotency}` once; no separate outbox table | `Edict.Core/Outbox/OutboxHost.cs:113` |
| Steady-state reminder footprint is zero | Lazy register-when-dirty, unregister-on-drain | `Edict.Core/Outbox/OutboxHost.cs:243` |
| Backoff has no shared scheduler | Per-entry `NextAttemptUtc` + the same lazy reminder; no second timer subsystem to contend with | `Edict.Core/Outbox/OutboxBackoff.cs` |
| Backoff cannot stampede | Deterministic per-entry jitter from `EntryId` hash; no RNG, no clock | `Edict.Core/Outbox/OutboxBackoff.cs:53` |
| Claim-check is conditional, not universal | Small events ride the entry verbatim; only oversize pays the blob round-trip | `Edict.Core/ClaimCheck/ClaimCheckPolicy.cs:55` |
| Routing is a dict lookup, not reflection | `CommandRouteResolver` reads a generator-emitted `IReadOnlyDictionary<Type, CommandRoute>` | `Edict.Core/Commands/CommandRouteResolver.cs:41` |
| Per-aggregate independence | ADR 0026: a poison entry no longer wedges the rest of its grain's pending; throughput stays up under a partial failure | `Edict.Core/Outbox/OutboxHost.cs:180` |

---

## 3. Concerns — graded by burst-load severity

### 3.1 Summary table

| # | Concern | Severity at 5–10 k EPS | Surface | Shape of the fix |
|---|---|---|---|---|
| 1 | **N writes per N-event command** — `DrainAsync` calls `WriteStateAsync` after every successful ack | **Critical** | `Edict.Core/Outbox/OutboxHost.cs:227` | Batch acks; one write per drain pass, not per entry |
| 2 | **`OutboxSlice` mutations are O(n) each, drain is O(n²)** — every `Enqueue`/`Ack`/`Promote` allocates a new `List<OutboxEntry>` via `[.. Pending, ...]` | High | `Edict.Core/Outbox/OutboxSlice.cs:30,44,72,91` | Switch to `ImmutableList<T>` or carry a `(List<T>, int head)` discriminator; bulk enqueue API |
| 3 | **Reflection per event publish** — `EventStreamAddress.Resolve` re-reads `[EdictStream]` + scans properties via `GetCustomAttribute` / `GetProperties` on every call, and is called twice from `ClaimCheckPolicy` | High | `Edict.Core/Outbox/EventStreamAddress.cs:21–29`, `Edict.Core/ClaimCheck/ClaimCheckPolicy.cs:73–74` | Cache `(streamName, PropertyInfo)` per `Type` in a `ConcurrentDictionary`; or have the generator emit a static accessor map (no reflection at runtime) |
| 4 | **Double / triple serialisation** of the same event on the publisher path: serialise in `ClaimCheckPolicy.ApplyAsync` → store bytes in `OutboxEntry.Payload` → deserialise in `PublishEventExecutor` to stamp `EventId`/`OccurredAt`/trace, then push to stream (which serialises again into the stream-provider frame) | High | `Edict.Core/ClaimCheck/ClaimCheckPolicy.cs:54`, `Edict.Core/Outbox/PublishEventExecutor.cs:28,42` | Stamp fields *before* the outbox serialisation; let the executor pass-through the bytes to the stream-provider frame |
| 5 | **`ServiceProvider.GetRequiredService<Serializer>()` per staged entry** in `EdictEventHandler`, `EdictSaga`, `EdictTableProjectionBuilder` | Medium | `EdictEventHandler.cs:90`, `EdictSaga.cs:113`, `EdictTableProjectionBuilder.cs:129` | Cache the resolved service in a field on first use (it never changes for the lifetime of the grain) |
| 6 | **Dedup-ring `Contains` is O(ring size) per delivery** — `Array.IndexOf` linear scan | Medium (Low at default `RingSize=100`; High for sized-up singletons) | `Edict.Core/Idempotency/EdictIdempotencyBase.cs:285,288` | Mirror the ring with a `HashSet<Guid>` on activation; ring stays the canonical persisted state, set is the in-memory accelerator |
| 7 | **`Restart from head` after every successful ack** — the drain walks back from index 0 each success | Medium | `Edict.Core/Outbox/OutboxHost.cs:230` | Track an `enqueuedDuringDrain` flag; only restart if anything new arrived |
| 8 | **No batch publish to the same stream** — N events to the same `(streamName, routeKey)` from one command are N separate `IAsyncStream.OnNextAsync` calls | Medium | `Edict.Core/Outbox/PublishEventExecutor.cs:51` | Group entries by stream key in the drain; one `OnNextAsync(IEnumerable<T>)` call per group where the provider supports batching |
| 9 | **JSON-inside-Orleans for `UpsertRowEffect.RowJson`** — row goes through `JsonSerializer` *then* Orleans's binary codec when the entry is persisted | Medium | `Edict.Core/Projections/EdictTableProjectionBuilder.cs:119`, `Edict.Core/Outbox/UpsertRowExecutor.cs:62` | Persist the row as Orleans-serialised bytes (its own `[GenerateSerializer]`); single codec; smaller bytes |
| 10 | **`Activity.Current` and span creation per event** at default sampling | Medium (operator-tunable) | `EdictDiagnostics.ActivitySource`, every executor + base | Document sampling guidance at 5–10 k EPS; consider a "fast-path no-span" mode for `claim-check.put`/`get` when the parent isn't recording |
| 11 | **`new Guid[RingSize]` reallocation if `RingSize` ever changes** | Low | `EdictIdempotencyBase.cs:275` | Cheap once on activation; only a problem if a consumer toggles `RingSize` mid-life, which they shouldn't |
| 12 | **`new ValidationContext<TCommand>` per command** for FluentValidation | Low | `EdictCommandHandler.cs:146` | Inherent to FluentValidation; document if it becomes hot |
| 13 | **`Guid.NewGuid()` per outbox entry** | Low | every entry construction | Cryptographic RNG cost; trivial at this scale, but visible in profiles |

### 3.2 Critical and high-severity concerns

#### 3.2.1 N storage writes per N-event command (`OutboxHost.DrainAsync`)

`Edict.Core/Outbox/OutboxHost.cs:227` — after each successful executor, the drain `Ack`s and calls `await _state.WriteStateAsync()` *per entry*. A command that raises 5 events pays:

| Phase | `WriteStateAsync` calls |
|---|---|
| Enqueue (`EnqueueAndDrainAsync`:120) | 1 |
| Drain ack of event #1 | 1 |
| Drain ack of event #2 | 1 |
| … through event #5 | 3 |
| **Total** | **6** |

At 5 k commands/sec each raising 3 events on average, that is **20 k storage writes/sec from acks alone** — on Azure Table that's 20× your transaction count and 20× the latency budget. Worse, the *common case* is success, so this isn't paying for safety; it's paying for nothing.

Correctness today: each ack-write is what makes "this entry already shipped" survive a crash mid-drain. The cheaper construction is one write at the end of the drain pass, covering all acks together, with the understanding that a crash mid-pass will re-execute some already-shipped entries — which is *exactly the at-least-once contract Edict already commits to* (idempotency on the consumer side is mandatory by design). The single write per pass loses no safety; it just stops paying for safety twice.

| Approach | Writes per N-event drain | Crash exposure |
|---|---|---|
| **Today**: write per ack | N + 1 | Re-execute ≤ 1 entry on crash |
| Batch: write once at end of pass | 2 (enqueue + final) | Re-execute ≤ N entries (already idempotent) |
| Hybrid: write every K acks or every T ms | ⌈N/K⌉ + 1 | Re-execute ≤ K entries |

A `BatchAckWindow` option (entries-per-write threshold) would let operators tune the writes-vs-replay tradeoff without changing the durability story.

#### 3.2.2 `OutboxSlice` mutations are O(n) per call; drain is O(n²)

`Edict.Core/Outbox/OutboxSlice.cs:30,44,72,91` — every transition is `this with { Pending = [.. Pending.Take(i), bumped, .. Pending.Skip(i+1)] }` or similar. That allocates a fresh `List<OutboxEntry>` of length `n` per mutation.

| Path | Allocations |
|---|---|
| `EnqueueAndDrainAsync` over a batch of M entries (the `foreach` at OutboxHost.cs:115) | M `Enqueue` calls × O(current `n`) → O(M·n) for one command |
| `DrainAsync` walking N entries, all succeeding | N `Ack` calls × O(remaining) → O(N²) |
| A pending list of 20 under burst | 20 + 19 + … + 1 = 210 list allocations per drain |

Two cheap moves:

1. **Bulk enqueue API** — `OutboxSlice.EnqueueRange(IReadOnlyList<OutboxEntry>)` that returns `this with { Pending = [.. Pending, .. entries] }` once. Drops the `EnqueueAndDrainAsync:115` loop's O(M²) cost.
2. **`ImmutableList<OutboxEntry>`** — structural sharing makes `Ack`/`Promote` O(log n) instead of O(n), at the cost of slightly worse iteration constants. At sustained burst with deep pending lists this is the better default; at shallow lists the constant overhead loses. Worth benchmarking before committing.

Or keep the mutable `List<>` but represent `Ack` as a "tombstone + sweep on drain end" — no per-success copy.

#### 3.2.3 Reflection per event publish (`EventStreamAddress.Resolve`)

`Edict.Core/Outbox/EventStreamAddress.cs:21–29`:

```csharp
var streamAttr = (EdictStreamAttribute?)Attribute.GetCustomAttribute(type, typeof(EdictStreamAttribute)) ?? throw …
var routeKeyProp = Array.Find(type.GetProperties(BindingFlags.Public | BindingFlags.Instance), p => Attribute.IsDefined(p, …)) ?? throw …
return (streamAttr.Name, (Guid)routeKeyProp.GetValue(evt)!);
```

`GetCustomAttribute`, `GetProperties`, `Array.Find`, `Attribute.IsDefined`, `PropertyInfo.GetValue` (boxed Guid back-out) — **per event, per publish**. And `ClaimCheckPolicy.ApplyAsync` calls it *twice* in the oversize branch (lines 73, 74) for the inner-stream-name and route-key.

| Fix | Cost | Effort |
|---|---|---|
| Cache `(streamName, PropertyInfo)` per `Type` in a `ConcurrentDictionary<Type, (string, Func<EdictEvent,Guid>)>` keyed on first sight; bake a compiled `Func<EdictEvent,Guid>` for the route-key getter | One reflection hit per type, then O(1) | Small |
| Have the generator emit a static `EventStreamAccessors` table — `Dictionary<Type, (string, Func<EdictEvent,Guid>)>` populated from `[EdictStream]` + `[EdictRouteKey]` at compile time, registered into DI | Zero reflection at runtime | Medium (already in generator territory) |

At 10 k EPS the reflective approach is *probably* still survivable on a modern CPU (low microseconds per call), but the property scan + virtual `GetValue` is the kind of thing that turns into a 10–15 % CPU line in a flame graph for no reason. Cache it.

#### 3.2.4 Double / triple serialisation of the same event

The publisher path serialises an `EdictEvent` three times before it reaches the stream-provider's wire:

1. **`ClaimCheckPolicy.ApplyAsync`** (`Edict.Core/ClaimCheck/ClaimCheckPolicy.cs:54`) — `_serializer.SerializeToArray<EdictEvent>(evt)` to measure size and produce the outbox-entry payload.
2. **Persistence of the entry** — Orleans serialises the `OutboxEntry` (whose `Payload` is the bytes from step 1) into the grain-state codec.
3. **`PublishEventExecutor.ExecuteAsync`** (`Edict.Core/Outbox/PublishEventExecutor.cs:28`) — `serializer.Deserialize<EdictEvent>(entry.Payload)` to stamp `EventId`/`OccurredAt`/trace, then push to stream — which serialises *again* into the stream-provider's frame.

Steps 1 + 3 are the wasted ones. The pattern is "we serialised it on the way in, deserialised it on the way out, only to mutate three properties and re-serialise". Two cleaner shapes:

| Shape | Cost saved | Tradeoff |
|---|---|---|
| Stamp `EventId`/`OccurredAt` *before* the claim-check serialisation in `CommitAndDrainRaisedEventsAsync` | One deserialise/reserialise round-trip per event | `OccurredAt` becomes "enqueued at" rather than "published at" — semantically defensible (ADR 0011 doesn't pin it to wire time) and tests would prefer it |
| Carry the live `EdictEvent` (not bytes) on the entry until persistence, deserialise only on crash-recovery | Saves the *inline* drain case (steady state) | Adds a `[NonSerialized]` ephemeral field; complexity in the entry shape |

The first option is the smaller change. `OccurredAt = DateTimeOffset.UtcNow` at line `PublishEventExecutor.cs:45` is the only field that meaningfully needs a post-enqueue value, and even that is debatable — domain events generally use the *intent* time, not the *wire* time.

The InvokeHandler path has the equivalent shape: `EdictEventHandler.BuildInvokeHandlerEntry` serialises the envelope, `InvokeHandlerExecutor` deserialises it, then `ClaimCheckUnwrap` may deserialise the inline bytes a third time. Same fix, same place.

### 3.3 Medium-severity concerns

#### 3.3.1 `ServiceProvider.GetRequiredService<Serializer>()` per staged entry

`EdictEventHandler.cs:90`, `EdictSaga.cs:113`, `EdictTableProjectionBuilder.cs:129`, also `EdictIdempotencyBase.cs:175` — every entry-building path resolves `Serializer` from `ServiceProvider`. The serializer is a singleton; resolving it from MEDI is one `ConcurrentDictionary` lookup, but every-event-paying-for-it accrues.

Cache it in a field on first access (same pattern as `_host` and `_unwrap`). Small but free win.

#### 3.3.2 Dedup-ring `Contains` is O(ring size)

`EdictIdempotencyBase.cs:285,288` — `Array.IndexOf` is a linear scan. At the default `RingSize=100` this is invisible. At a sized-up singleton (`RingSize=50_000` for a fleet-wide audit consumer) it's a per-event O(50k) memory walk, dominated by cache misses, on the dispatch hot path.

A `HashSet<Guid>` mirror — rebuilt once on activation from the persisted ring, kept in sync as the ring rotates — turns `Contains` into O(1). The ring stays the canonical persisted state (mirror is in-memory only), so no schema change is needed.

This pairs with the resiliency analysis §3.3.2 "dedup ring is silently bounded" — both want operators to think about `RingSize` for singletons, and the perf fix removes the disincentive to size it up.

#### 3.3.3 Restart-from-head after every ack

`OutboxHost.cs:230` — every successful ack restarts the drain walk at `index = 0`. The intent (per ADR 0026) is to surface newly-enqueued entries immediately, but the path that does the enqueue (`EnqueueAndDrainAsync`) commits before calling `DrainAsync` — entries can only appear during a *re-entrant* enqueue, which happens only if an executor itself enqueues (it doesn't, by design).

So the restart is paying for a case that doesn't happen. A `bool _enqueuedDuringDrain` set inside `EnqueueAndDrainAsync` and checked at the top of the while-loop would let the drain walk forward monotonically in the common case, dropping the worst-case from O(N²) walks to O(N).

If re-entrant enqueue ever becomes a thing (a saga that dispatches a command which calls back into the same grain? unlikely but possible), the flag covers it.

#### 3.3.4 No batched publish to the same stream

`PublishEventExecutor.cs:51` — every entry's `stream.OnNextAsync(stamped)` is a single-message publish. A command that raises 3 events to the same domain stream (same `[EdictStream]` + same route key) pays 3 stream calls, which in the Azure Queue Storage provider is 3 enqueue requests — 3× the per-message billable cost and 3× the round-trip latency.

In the drain loop, group consecutive entries by `(streamName, routeKey)` and call the batch overload where the provider supports it. The grouping is local to one drain pass; entries from different commands can't be grouped (different traceparents, different intent times) — only events from the same `EnqueueRaisedEventsAndDrainAsync` call.

#### 3.3.5 JSON-inside-Orleans for `UpsertRowEffect.RowJson`

`EdictTableProjectionBuilder.cs:119` — the row is serialised with `System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(row)`, then the `UpsertRowEffect` (containing those JSON bytes) is Orleans-serialised when the outbox entry is persisted.

| Cost | Origin |
|---|---|
| Two serialisers warm in the same process | Tax on startup, GC churn on hot path |
| JSON bytes are 2–4× the Orleans binary equivalent | Bigger entry → bigger grain document → more substrate I/O per persist |
| `JsonSerializer.Deserialize` boxing at drain | One reflection-driven path per row write |

If `T` already requires `[GenerateSerializer]` (which it does — `IEdictPersistedState` requires it per ADR 0027 + EDICT011), the row can ride as Orleans bytes directly: `serializer.SerializeToArray(row)` here, `serializer.Deserialize<T>(bytes)` at drain. Same codec end-to-end, smaller payload, no second serialiser warmup.

#### 3.3.6 Telemetry exporter as a backpressure source

At 5–10 k EPS, every command opens a span, every publish opens a span, every handle opens a span — call it 3–5 spans per event-flow on average. 30–50 k spans/sec into an OTLP exporter is a meaningful workload for the exporter queue. If the exporter back-pressures, `Activity` objects accumulate in the queue and pay GC tax.

Two operator-doc items rather than code fixes:

- Recommend `TraceIdRatioBasedSampler(0.01)` (or lower) at this throughput.
- Recommend a batching exporter (`BatchActivityExportProcessor`) sized generously (queue size ≥ 10× the per-second span count).

The framework could also add a `claim-check.put`/`get` fast-path that skips the span when the parent isn't recording — those are inside the high-allocation hot path.

### 3.4 Low-severity items rolled up

`EnsureRingInitialized` per event, FluentValidation `ValidationContext` per command, `Guid.NewGuid()` per entry — each pays a few hundred nanoseconds, each visible on a flame graph, none individually severity-worthy.

---

## 4. Concerns you may not have considered

| Concern | Why it matters at 5–10 k EPS |
|---|---|
| **Singleton consumer is a single-thread bottleneck.** A fixed-Guid singleton (the global escape hatch) is one grain → one turn at a time → one event-handler invocation at a time. | If the consumer ever points an aggregating projection (e.g. "total orders today") at the singleton route, throughput is capped at one event per turn duration — call it ~5 k EPS on a fast handler, much less on a slow one. No analyzer warns; the failure mode is "throughput plateaus and nobody knows why". A diagnostic on activation that logs the singleton + EPS estimate would help. |
| **Dead-letter projection is a singleton table projection.** Every dead-letter promotion in the cluster lands on the same `EdictDeadLetterProjectionBuilder` grain (ADR 0022). | A bad deploy that dead-letters 1 % of 10 k EPS = 100 promotions/sec funnelling into one grain. The projection's Outbox sees 100 pending entries per second, draining at one-write-per-ack (§3.2.1). The mechanism that *records* the storm is itself a candidate to fall behind during the storm. Worth either partitioning the dead-letter projection by event-type-name hash or — easier — combining with §3.2.1 so the ack-batching helps the dead-letter case too. |
| **Reminder period floor is hardcoded at 1 minute.** | A fleet-wide downstream outage registers a reminder per dirty grain. With 100 k dirty grains, that's 100 k reminder rows ticking at 1 Hz total (one minute period each, smeared). Orleans's reminder service is one cluster-wide table; sizing is the operator's, but the *recovery latency floor* of 1 minute is invisible from the consumer surface. Make `DrainReminderPeriod` an option on `EdictOutboxOptions`. |
| **`grainKey` and `grainTypeName` captured into every entry's dead-letter promotion via closure on the host.** | The closure holds a string and a `Type?` for the host's lifetime — fine. But `_promoter.Promote(...)` allocates a `EdictDeadLetterRaised` per failing entry; under storm this is non-trivial. Pool the promotion payload, or let `IDeadLetterPromoter` accept a struct context to avoid the per-call allocation. |
| **Implicit subscription cost grows with handler count.** Every `EdictEventHandler` subclass adds its `[ImplicitStreamSubscription]` rule. Orleans evaluates these on every event delivery to decide which grains to activate. | At 50+ event-handler types and 10 k EPS, the per-delivery subscription-routing cost becomes measurable. Generator-emitted attributes already minimise the rule shape, but worth a benchmark before consumers ship many dozens of handlers. |
| **Per-event `Activity.Current?.TraceId.ToHexString()` allocates a string.** | At 10 k EPS, that's 10 k string allocations per second from each of the trace-capturing sites in `EdictSaga`, `EdictEventHandler`, `EdictTableProjectionBuilder`. Cache or use `Span<char>`-based helpers. |

---

## 5. Bursts (5 k → 10 k spike)

The framework's burst behaviour is largely determined by Orleans's stream-provider backpressure, not by Edict's own code. The stream-provider pulling agents smear the burst across consumers, dedup absorbs the redelivery, lazy reminders keep steady-state quiet. **The amplification factor under burst is the per-entry write tax in §3.2.1** — if a burst of 10 k events lands and each event-handler raises one follow-up event (a normal saga pattern), the storage write rate is **10 k × (1 enqueue + 1 ack) × 2 hops = 40 k writes/sec**, four times the event rate. Fixing the ack-batching collapses this to ~20 k writes/sec.

The second amplification is the **dead-letter projection** during a burst-plus-poison scenario: 1 % poison events at 10 k EPS = 100 promotions/sec into a singleton grain (§4). The combination of §3.2.1 + §4 dead-letter-fanout is where the framework's burst story is most fragile, and both are addressable.

---

## 6. Headline summary and priority order

The framework is **correctness-first** by design and it shows in the code; the perf shape that pays for that correctness is *mostly* fine, with three notable exceptions where the mechanism costs more than the work it's doing: per-ack writes, per-event reflection, and per-event double serialisation. None of these are architectural — all three are local fixes inside `Edict.Core` with no consumer-facing API change.

| Priority | Item | Why it tops the list |
|---|---|---|
| 1 | **Batch ack-writes in `DrainAsync`** (§3.2.1) | Largest single-line throughput cliff. Drops storage writes per N-event command from N+1 to 2 with no safety loss. |
| 2 | **Cache `EventStreamAddress.Resolve`** (§3.2.3) | Per-event reflection on the hot path, easy to remove with a `ConcurrentDictionary<Type, …>` or a generator-emitted table. |
| 3 | **Collapse double-serialisation on the publisher path** (§3.2.4) | Saves CPU and Gen0 allocation per event; defensible semantic adjustment to `OccurredAt`. |
| 4 | **Bulk enqueue + reconsider `OutboxSlice` representation** (§3.2.2) | O(n²) drain becomes O(n); shows up most under burst, exactly when it matters. |
| 5 | **HashSet-mirror for dedup ring + sized singleton guidance** (§3.3.2) | Removes the perf disincentive to size singletons correctly; pairs with the resiliency-analysis ring-size concern. |
| 6 | **Dead-letter projection partitioning** (§4) | The mechanism that records the storm should not be a single grain. |
| 7 | **Operator docs: sampling + reminder-period tunable + singleton-grain caveat** (§3.3.6, §4) | Cheap, no code change in the framework runtime. |

A reasonable next step is to lock in 1–3 with benchmark coverage in `Edict.Core.Tests` (a `BenchmarkDotNet` project) before any optimisation: every claim above is the kind that survives only as long as the benchmark does.
