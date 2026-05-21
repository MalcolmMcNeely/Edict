# Edict in a Financial Trading System — Fit Analysis

**Status:** Theorycraft / discussion input. Not a decision, not an ADR.
**Scope:** Edict as a framework (`Edict.Contracts`, `Edict.Core`, `Edict.Telemetry`, `Edict.Azure`), evaluated for use as the application backbone of a financial trading system — order management, execution, position keeping, post-trade workflows, regulatory reporting. The Sample app and the shipped `Edict.Testing` harness are out of scope.
**What "trading system" means here:** a venue-facing buy-side or sell-side OMS/EMS that takes client orders, applies pre-trade risk, routes to one or more execution venues, books fills, maintains positions and P&L, supports end-of-day reconciliation, and produces audit and regulatory artefacts (MiFID II RTS 24 order record-keeping, RTS 25 clock sync, RTS 27/28 best execution; FINRA OATS/CAT; SEC 17a-4 retention; Dodd-Frank swap reporting). Workload assumed: hundreds-to-thousands of new-order-singles/sec sustained, market-open bursts to ~10 k commands/sec, 99th-percentile order-to-ack budget in the low single-digit milliseconds for the OMS hop (the venue round-trip is not Edict's concern), 7-year retention floor on order and trade records.

The framework's correctness story (atomic envelope, per-entry retry with backoff, dedup window, dead-letter projection, claim-check for oversize payloads, parent-child trace stitching) is load-bearing across many ADRs. The question this document inventories is: **for a trading-system consumer, which of the system's needs does Edict already meet, which sit just outside Edict but are the consumer's domain to build, and which are framework-level holes that a trading-grade Edict would need to close?**

The analysis is deliberately uncomfortable with the easy answer ("just add a global stream"). Many regulatory and operational needs in trading have more than one defensible shape inside Edict's existing primitives, and the right shape often depends on a constraint that is invisible to the framework. Where that is the case the section names the constraint and shows the trade.

---

## 1. What Edict already gives a trading consumer for free

Worth naming up front, because most of the framework's existing mechanics are *exactly* what a trading system would build by hand if they were missing.

| Need | Edict mechanism | Why this is the right shape for trading |
|---|---|---|
| Pre-trade risk gate that *must* run before any state change and *must not* mutate state | `EdictCommandHandler.Validator` (FluentValidation, server-side, same activation turn as `Handle`) | Fat-finger checks, credit-limit checks, restricted-list checks, order-size sanity, kill-switch — all are admissibility checks against current aggregate state and a `Rejected` outcome is a first-class result, not an exception. Same-turn execution means a parallel command cannot race the gate. (ADR 0009) |
| Atomic "decision + emitted events" on the order aggregate | `GrainEnvelope { Payload, Outbox, Idempotency }` — one grain-document write covers state mutation and every raised event | An accepted order that fans out `OrderAccepted`, `RiskCheckPassed`, `RoutedToVenue` lands as one atomic write. There is no window where the order is "accepted" but the downstream events have not been recorded. (ADR 0018, ADR 0025) |
| At-least-once delivery with consumer-side dedup | `EdictIdempotencyBase` bounded window of recently handled `EventId`s, committed after `HandleAsync` | A redelivered `OrderFilled` to the position keeper does not double-increment the position. (ADR 0002) |
| Distributed trace stitching from command intake to terminal handler | Trace fields on `EdictEvent`, parent-child span across the stream hop via `RequestContext` | A single trace covers "client API → OMS aggregate → execution-router event → fill-handler → position update", which is what every front-office investigation actually wants. (ADR 0003) |
| Forensic record of effects that permanently failed | `EdictDeadLetterProjectionBuilder` + `IEdictDeadLetterRepository` | A poison fill, a venue-ack timeout that exhausted retries, a downstream blob-missing — all land in a single queryable table with the full failure context. (ADR 0022) |
| Oversize payloads (large client-instruction blobs, structured product term sheets) | Claim-check pointer-bearing envelope on the wire, automatic fetch on the receiver | A 200 KB FpML payload does not break the 32 KB per-property cap or the queue body cap. (ADR 0024) |
| Aggregated current-state read models (positions, balances, open-order book per account) | `EdictTableProjectionBuilder` with per-partition rows, exposed read-only via `IEdictTableRepository` | The position screen reads from a table, not from the order grain. Hot-path reads do not page in cold aggregates. (ADR 0015) |
| Multi-step workflows (order lifecycle, allocations, settlement chase) | `EdictSaga<TProgress>` with `Dispatch` (exactly one command per event) | The "one command per event" hard limit nudges complex flows into a chain of single-step sagas rather than a fan-out blob — which is the shape post-trade workflows actually take when you draw them. (ADR 0020) |
| Backpressure-tolerant outbound effects under venue or downstream outage | Outbox per-entry independent retry, lazy reminder net, exponential backoff | A 30-minute venue outage does not wedge the order aggregate's intake; the outbox holds the pending publishes and drains when the venue recovers. (ADR 0026) |
| Single configuration surface, fail-at-start on misconfiguration | Three `ISiloBuilder` extensions with `Action<T>` options, `ValidateOnStart` everywhere, missing-provider hosted-service check | A half-wired silo cannot start carrying real orders. (ADR 0028) |

The *shape* of the framework — CQRS with a hard split between commands (direct grain calls) and events (streams), explicit aggregate boundaries, idempotent event consumers — is already the shape every well-built OMS converges on. A trading-system consumer is not fighting Edict's grain.

---

## 2. Where Edict ends and the consumer begins — boundary lines that matter

Most "gaps" people will name in a trading context are **not framework gaps**. They are domain modelling, regulatory interpretation, and integration work that Edict is deliberately not in scope to do. Naming the boundary up front is the only way to keep the rest of the document honest.

### 2.1 Consumer-owned, full stop

These are the consumer's responsibility under any framework, and Edict is correct not to ship opinions about them.

| Concern | Why it is consumer-owned |
|---|---|
| **The order, trade, position, account aggregate models** | Domain modelling — every firm's product mix, asset coverage, and risk policy is different. Edict ships the grain bases (`EdictCommandHandler<TState>`); the consumer writes the `Order` aggregate. |
| **Pre-trade risk rules** | The *gate mechanism* is `Validator`; the *rules themselves* (credit checks against a counterparty exposure cache, locate checks for shorts, restricted-list lookups, single-name and portfolio limits) are domain logic. |
| **Best-execution policy** | RTS 27/28 reporting is a regulatory output; the *policy* that picks a venue per order is the consumer's quant team's IP. |
| **Venue connectivity** (FIX/binary protocol adapters) | Edict has no business owning a FIX engine. The pattern is: an `EdictEventHandler` reacts to `RoutedToVenue`, calls into a FIX session host (a separate process, gRPC, or in-process library), and uses `evt.EventId` as the `ClOrdID` correlator so the session is idempotent under at-least-once redelivery. |
| **Reference data** (instruments, calendars, tick sizes, corporate actions) | Read-side concern; the consumer maintains its own reference-data store or uses a vendor feed. Edict has no opinion on whether reference data is in a grain, a projection, or an external service. |
| **Market data plumbing** | High-volume, low-latency, fundamentally a different traffic shape than the command/event flow Edict is built for. Trading on market data is fine inside Edict (an event handler that reacts to a `PriceTick` could dispatch a `CancelOrder` command); ingesting raw market data through the outbox is the wrong tool. |
| **End-of-day reconciliation** | A scheduled job that reads the trade projection and a counterparty file and writes a break report. Sits *above* Edict. |
| **Authentication and authorisation** | Identity, role-based and entitlement-based access, four-eyes approvals on supervisor overrides — all sit at the API edge and the validator, not in the framework. |
| **PII handling, GDPR erasure, tokenisation** | The consumer chooses what to put in command and event payloads; Edict treats them as opaque MessagePack. (See §3.7 for one place this *does* touch the framework: append-only claim-check blobs.) |
| **KYC / AML screening** | Validator or an upstream service; not Edict's concern. |
| **Settlement instructions, custody, corporate actions processing** | Post-trade workflows are sagas the consumer writes; Edict provides the saga base. |

A useful heuristic: **if the concern would exist on a green-field trading system regardless of which framework was chosen, it is consumer scope.** Edict is a *substrate*; the trading system is a *product* built on it.

### 2.2 Consumer-owned but framework-adjacent

These are consumer responsibilities, but Edict's existing primitives shape what the consumer can defensibly build. The framework choice is whether to leave the shape implicit (today) or to surface guidance / a thin helper.

| Concern | Today | Framework-adjacent question |
|---|---|---|
| **Order/trade audit log retention for 7 years** | The consumer writes a Table Projection that records every event. SEC 17a-4 WORM compliance is the storage account's policy, not Edict's. | Should Edict ship an opinionated "audit-everything" projection base, or at least an analyzer that warns when the consumer has no projection subscribed to a stream that carries `EdictCommand`-derived events? See §4. |
| **Clock synchronisation for `OccurredAt`** (MiFID II RTS 25: 100 µs of UTC for venue-facing flow, 1 ms for the rest) | `OccurredAt = DateTimeOffset.UtcNow` at publish (`PublishEventExecutor.cs:45`), no PTP/NTP guarantee, no monotonic clock | Edict could expose `TimeProvider` consistently (it already does for backoff) and document the obligation; the *sync* itself is an operations problem, not framework code. |
| **Sequence numbers on the audit trail** | `EventId` is a Guid; there is no monotonic sequence. The order of events on a stream is `OccurredAt`-loose under retry (ADR 0026). | A trading audit needs a strict, monotonic, gap-free sequence *per aggregate at minimum* and often *globally*. The consumer can stamp one inside `Handle` before `Raise`, but that is a foot-gun (two `Handle` invocations on the same aggregate cannot collide because of grain turn discipline; cross-aggregate global numbering needs a coordinator and is not free). See §3.4. |
| **Per-aggregate vs global event ordering for the order book** | Per-aggregate is preserved on the happy path, reordered on the retry path (ADR 0026). Global ordering is not provided at all. | A central-limit order book that depends on global FIFO across many orders is **not** a fit for Edict's distributed grain model and **should not** be forced into one. The order book is a single matching engine; Edict is the substrate around it. |
| **Idempotency keys to external venues** | The consumer uses `evt.EventId` as `ClOrdID`/`Idempotency-Key`. | Edict already documents this for event handlers; a trading-grade extension might surface it on a typed `IdempotencyKey` property on `EdictEvent` to make the obligation explicit. |
| **Replay for regulatory inquiry** ("show me every state this order was in on 2026-03-14") | Edict is event-driven, not event-sourced — there is no replay (ADR 0001). The audit-log table projection *is* the record. | The consumer materialises an event-by-event log via a Table Projection. The framework is correct not to offer replay; the obligation lands on the consumer's projection design. See §3.1. |

### 2.3 Things that *look* like Edict gaps but are not

Worth calling out explicitly because they will come up in any trading review.

- **"Edict doesn't have transactions across aggregates."** Correct, and so does no grain framework — Orleans gives one-grain-document-write atomicity and nothing more. Cross-aggregate consistency in trading is a *saga* problem (the consumer writes a `TradeBookingSaga` that coordinates `Order`, `Position`, `Cash` aggregates), not a framework gap.
- **"Edict doesn't enforce 7-year retention."** Retention is a storage-policy concern on Azure Blob and Azure Table. Edict only writes; the consumer's lifecycle policy retains. The same applies to legal hold and WORM.
- **"Edict doesn't have a market-data feed."** Correctly out of scope.
- **"Edict's at-least-once means we might double-book a trade."** No — the consumer's fill-handler is idempotent on `evt.EventId`, which is exactly what Edict's dedup window + external-API idempotency key pattern is designed for. The framework is correct to make the obligation explicit rather than promise exactly-once.

---

## 3. Edict gaps that bite a trading consumer

These are framework-level concerns — places where a trading consumer would be either fighting Edict, working around a missing primitive, or building infrastructure that arguably belongs inside the framework. Graded by how badly each bites at the assumed workload (low-thousands sustained, ~10 k market-open burst).

### 3.1 Severity table

| # | Gap | Severity | Why it bites |
|---|---|---|---|
| 1 | No first-class **immutable per-event audit projection** — the consumer must build it from `EdictTableProjectionBuilder`, choose a partition scheme, and accept the dedup-window-equals-row-write atomicity caveat | **High** | RTS 24, SEC 17a-4, CAT, OATS — all require an immutable record of every order event with low-double-digit field sets. Every trading-system consumer of Edict will build the same projection; building it wrong (e.g. using a singleton aggregator) costs them throughput, and the framework gives no analyzer or guard |
| 2 | **Singleton consumer is a single-thread bottleneck** for global ordering / global audit | **High** | A consumer who reaches for a fixed-Guid singleton "global audit log" caps fleet throughput at one event per grain turn — exactly the wrong choice in a market-open burst. There is no diagnostic; the failure mode is "throughput plateaus and nobody knows why" |
| 3 | **No monotonic per-aggregate or global sequence number** on `EdictEvent` | **High** | A regulatory audit row is `(seq, ts, type, key, payload)`. Edict gives `(EventId Guid, OccurredAt, type, route key, payload)` — no monotonic seq, and `OccurredAt` is `DateTimeOffset.UtcNow` at publish, not at intent. Gap-free per-aggregate seq is recoverable (grain turn discipline) but the consumer must add it; global seq is hard |
| 4 | **`OccurredAt` is wire time, not intent time** (`PublishEventExecutor.cs:45` stamps it post-deserialise during drain) | **High** | RTS 25 clock-sync requirements are about the time the event *happened* in business logic, not the time the outbox drained. Under a backoff or a brief outage, wire time and intent time diverge by minutes. A regulator will not accept "we drained late" |
| 5 | **No native rate-limiting / throttling primitive** per venue, per client, per instrument | **High** | Venues throttle (CME MDP capacity limits, NYSE OUCH per-session rate caps); clients have per-second order caps in their agreements. The consumer can build it in a saga, but every consumer rebuilds the same logic. The shape (token bucket with per-key state) does not fit naturally in any current grain base |
| 6 | **No native circuit breaker** for downstream effects (venue session, market-data adapter, post-trade STP feed) | **High** | When a venue goes flaky, the outbox keeps retrying for `MaxAttempts` per entry — flooding a sick downstream. Per-target circuit breakers (open/half-open/closed) would back off the *whole stream*, not just the one entry. Today the engine is per-entry and stream-blind |
| 7 | **Dead-letter projection is singleton** — under a poison-event storm every promotion lands on one grain | **High** | A bad market-open deploy that dead-letters 1 % of 10 k EPS = 100 promotions/sec into one grain. The mechanism that *records* the storm becomes the bottleneck during the storm. Already flagged in resiliency analysis; for trading it is a regulator-visible failure mode (incident reporting is on the dead-letter table) |
| 8 | **No native "reject after age" / order TTL** on commands or saga steps | **Medium** | A `PlaceOrder` that sits in a slow validator queue for 200 ms is no longer the order the client wanted — the price has moved. Today the consumer adds a `DateTimeOffset Expiry` field and checks it in the validator manually; every consumer does this |
| 9 | **No native "kill switch" for an aggregate or stream**, separate from individual command rejection | **Medium** | A risk officer wants to halt *all* orders for an account in one click — a global intake gate, not 1000 individual `Reject`s. Today the consumer adds a `KillSwitch` aggregate that every validator reads; cross-cutting reads add latency |
| 10 | **No partitioned-stream primitive** for "audit log" / "tap" topology | **Medium** | A consumer who wants an audit projection that scales horizontally has to write per-aggregate projections (one row per aggregate, fine but slow to query) or shard manually by `route_key.GetHashCode() % N` into N grains (works but the consumer rolls it themselves). A framework "sharded singleton" primitive would help |
| 11 | **Outbox latency floor is several stream-hop round-trips**, not microseconds | **Medium** | OMS-internal "command-accepted → outbound FIX" latency, on Edict, is one grain-write + one stream publish + one stream-callback dispatch + one outbox-drain ack-write. At Azure Table speeds that is 20–80 ms p99. For HFT this is disqualifying; for institutional flow it is fine. The framework should be explicit that the latency floor is "milliseconds, not microseconds" |
| 12 | **No native end-of-day reconciliation primitive** — there is no concept of "freeze and snapshot at 17:00 ET" | **Medium** | Trading systems need a point-in-time consistent snapshot for T+1 reconciliation. Today the consumer reads the position projection at end-of-day and prays no late event mutates it during the read. A framework-level "consistent snapshot" affordance is hard but the absence is felt |
| 13 | **Wire-format evolution path is implicit** — `EdictEventEnvelope` is the documented evolution point (ADR 0024) but there is no shipped schema-version tag, no contract for additive vs breaking changes | **Medium** | A trade-event payload that lives in the audit table for 7 years will outlive any code that wrote it. The consumer needs a story for "we added a field in 2027 to satisfy a new CFTC rule" that does not break the 2024 events still being queried |
| 14 | **No PII redaction / data-minimisation primitive** on the outbox or claim-check store | **Medium** | GDPR Article 17 erasure on a 7-year audit trail is a nightmare on Edict today: claim-check blobs are append-only by design (ADR 0024), the dead-letter projection captures full payloads, the consumer's own audit projection captures full payloads. There is no framework hook for "redact this field before persist" |
| 15 | **No native correlation between linked commands** (parent order → child orders → fills) beyond trace context | **Medium** | A "parent order → 4 child orders to 2 venues → 12 fills" tree is a first-class trading concept. Trace context (ADR 0003) stitches the spans, but trace context expires and is not a queryable persistent identifier. Today the consumer adds a `ParentOrderId` field on every event; the framework gives no help |
| 16 | **No native event-time vs ingestion-time skew detection** | **Low** | Stream-processing systems usually surface "your stream is N minutes behind real time" as a metric. Edict has spans per event but no aggregate "lag" metric on a stream. The consumer adds a Prometheus gauge themselves |
| 17 | **`Edict.Telemetry` has one `ActivitySource` named `"Edict"`** — every span goes through it | **Low** | A trading consumer who wants to sample order-routing spans at 100 % and validator spans at 1 % must filter by tag, not by source. Workable, but noisier than per-component sources |

### 3.2 High-severity items, detailed

#### 3.2.1 No first-class immutable per-event audit projection

This is the one a trading consumer will hit hardest, fastest, and is the one most worth opinionating about at the framework level.

Every order event must be recorded immutably with the field set MiFID II RTS 24 mandates (around 65 fields including `InstrumentIdentifierCode`, `BuySellIndicator`, `Quantity`, `Price`, `TraderId`, `ClientId`, `DecisionMakerCode`, `ExecutionDecisionMakerCode`, `TradingCapacity`, `LiquidityProvisionFlag`, `OrderRestrictionFlag`, `ValidityPeriod`, `OrderClassification`, et cetera). The same applies, with different field sets, to FINRA CAT (US equities/options, ~30 fields per event), OATS (deprecated 2022 but still feeding the 7-year window), SEC 17a-4 (broker-dealer record retention with WORM), Dodd-Frank Part 45 (swaps reporting), MAS 610 (Singapore), and so on.

In Edict, the natural shape is an `EdictTableProjectionBuilder` consuming the order stream. The choice the consumer faces:

| Shape | Throughput | Audit completeness | Operational fit |
|---|---|---|---|
| **Per-aggregate projection** (the default — one grain per `Order` Guid, one row per order containing the latest state) | High — partitioned by Guid, scales with silos | **Wrong shape** — RTS 24 is per-*event*, not per-current-state. A "latest state" projection drops the history a regulator wants | Right shape for the order-book UI; wrong shape for audit |
| **Per-aggregate event log** (one grain per `Order` Guid, projection writes a row per event with `(orderId, seq, eventType, ts, payload)`) | High — same partitioning | Correct field set if the consumer captures every field — but cross-aggregate queries ("every order from Trader X on Tuesday") fan out to every aggregate's rows | Operationally fine on Azure Table (PartitionKey = `orderId`, RowKey = `{seq:D20}`); regulator queries usually have an aggregate key anyway |
| **Singleton global audit projection** (fixed-Guid grain receiving every event) | **One grain → one event-handler turn at a time → caps throughput at ~thousand EPS on a fast handler** | Correct — single ordered log | **Wrong** for the assumed workload; this is the singleton-bottleneck failure mode (§3.2.2) |
| **Sharded audit projection** (N grains, route by `hash(routeKey) % N`) | High — scales with N | Correct field set; cross-aggregate queries hit O(N/log) partitions | **The right shape for trading scale**, but the consumer rolls the sharding manually; the framework has no primitive |
| **Per-event-type singleton** (one grain per event type, all `OrderAccepted`s into one grain, all `OrderFilled`s into another) | Bottlenecks on hot event types (`OrderAccepted` is the highest-volume event) | Correct field set; queries naturally scope to event type | Workable but the hot-event-type bottleneck is the same singleton failure mode in miniature |

**The framework gap is twofold:** (a) there is no documented "trading audit" shape — every consumer rediscovers the sharded projection idea; (b) the dedup-window-equals-row-write atomicity guarantee (ADR 0012, amended by ADR 0026) holds only for inline-payload envelope delivery, not pointer-bearing (claim-check) delivery. For audit, a 200 KB FpML payload over claim-check means the audit row write happens *after* the dedup-window commit, and the entry can permanently dead-letter — leaving the audit table missing a row that the regulator expects to be there. That is not necessarily a regulator-fatal hole (the dead-letter projection records the miss), but it is a hole the consumer must understand and operationally close.

**Shape of the fix.** Two interlocking pieces:

1. **Ship a `EdictAuditProjectionBuilder<TEventBase>` base** that subscribes to a domain stream, computes a partition key from `hash(routeKey) % shardCount`, writes `(partition, {ticks:D20}-{eventId}, payload, headers)` rows, and exposes a query API on `IEdictTableRepository` shaped for audit (filter by time range, by event type, by trader, by aggregate key — paginated). This is *opinionated infrastructure* the consumer should not have to invent.
2. **Tighten the pointer-bearing audit invariant.** Either special-case the audit projection the same way the dead-letter projection is special-cased (store the pointer, not the body — ADR 0024 already has this shape) and let the regulator-facing query materialise on demand, or require the consumer to accept the dead-letter promotion path as the safety net (with operational SLOs on dead-letter rate).

#### 3.2.2 Singleton-consumer single-thread bottleneck

The fixed-Guid singleton is the explicit escape hatch for global read models (CONTEXT.md, §"Domain Stream"). It is one grain → one Orleans turn at a time → one event-handler invocation at a time. At 10 k EPS, even a handler that takes 100 µs caps throughput at the turn rate, which is dominated by the grain-state write — call it 5–20 ms on Azure Table — so the singleton ceiling is roughly **50–200 EPS sustained, two orders of magnitude below the assumed workload**.

A trading consumer who reaches for a singleton "global audit log" or "global risk aggregator" will not see this in development (low EPS) and will see it as a throughput plateau on the first market-open burst. There is no analyzer warning. The framework's documentation calls it the "explicit escape hatch" but does not name the throughput ceiling.

**Shape of the fix.** A `[EdictGlobalSingleton]` attribute (or detection of a fixed-Guid stream subscription pattern) that emits an analyzer warning naming the throughput ceiling and pointing to the sharded-projection pattern. Or, more ambitiously, a `EdictShardedSingleton<N>` base that materialises as N grains with the route-key hash partition, exposed as if it were one logical consumer.

#### 3.2.3 No monotonic per-aggregate or global sequence number

`EventId` is a fresh `Guid.NewGuid()`. `OccurredAt` is `DateTimeOffset.UtcNow`. Neither is monotonic; neither is gap-free. Regulators expect gap-free per-aggregate sequences ("show me events 1..N for order X with no gaps and explain any missing seq") and many vendor audit-trail formats require a global monotonic sequence column.

The grain turn discipline gives the consumer everything needed for **per-aggregate gap-free seq** — a single `long Seq` field on the aggregate state, incremented in `Handle` before `Raise`, stamped onto the event. The framework just doesn't show them how, and the naive implementation forgets to make `Seq` part of the persisted-state contract.

**Global monotonic seq** is genuinely hard on a distributed grain system. Two defensible shapes: (a) a singleton `SequencerGrain` that hands out monotonic longs (back to the singleton-bottleneck problem); (b) hybrid logical clocks (HLC) that give *causally monotonic* timestamps without a coordinator, which is what most modern distributed audit systems use. (b) is a real framework-level decision and a real piece of code.

**Shape of the fix.** Ship an `EdictSequencedEvent : EdictEvent` base with a `long PerAggregateSeq` property the framework populates from grain state, and an HLC-backed `long GlobalSeq` for systems that need it. Make it opt-in.

#### 3.2.4 `OccurredAt` is wire time, not intent time

`PublishEventExecutor.cs:45` sets `OccurredAt = DateTimeOffset.UtcNow` at drain time. For a happy-path drain (microseconds after `Raise`) this is fine. For an event that sat in the outbox under backoff for 30 seconds because the stream provider was throttling, `OccurredAt` is 30 seconds after the business event actually happened.

For a regulator, this is a real problem. RTS 25 requires the timestamp to be the time the event "occurred" — which the regulator means as the business event, not the wire transmission. Best execution analyses depend on `OccurredAt - venueAck` being the OMS-internal latency; if `OccurredAt` is wire time, that latency is reported as zero and the analysis is wrong.

**Shape of the fix.** Stamp `OccurredAt` in `Raise` (synchronous in-memory buffer call) before the event is serialised into the outbox entry. This is also a perf win (one fewer reason to deserialise the entry at drain time) and is mentioned in the performance analysis as worth doing for unrelated reasons. For trading it is a correctness fix.

#### 3.2.5 No native rate-limiting / throttling primitive

Every venue session has a per-second order cap. Every client agreement has a per-second order cap. Crossing the cap is a *protocol-level reject* from the venue (NACK), which today in Edict would trigger the outbox retry path — making the storm worse — and would dead-letter after `MaxAttempts`. The consumer needs a token bucket per `(venue, session)` and per `(client, instrument-class)` that gates the outbound effect before the venue sees it.

A trading consumer can build this:

- A `RateLimiter` grain per `(venue, session)` key, queried by the venue-routing `EdictEventHandler` before the FIX call.
- The grain holds a token-bucket state (capacity, refill rate, last refill time, current tokens).
- The handler calls `tryAcquire` synchronously; if false, it raises a `RoutingDeferred` event and the saga retries on a timer.

This works, and is the right shape, but every consumer writes it from scratch, the grain-call overhead per outbound order adds latency on the hot path, and the token-bucket state is not naturally durable across grain deactivation.

**Shape of the fix.** A `EdictRateLimiter` primitive (a grain base, or a stateless extension on `IEdictSender`) that wraps a key with a configured token bucket. The token bucket lives in grain state; the API is `Task<bool> TryAcquireAsync(string key)`. This is the kind of utility that *every* venue-touching consumer needs and that the framework should ship rather than expect each shop to reinvent.

#### 3.2.6 No native circuit breaker

The outbox today is per-entry: a failing entry retries on its own clock with its own backoff. If a venue session goes flaky, *every* entry to that venue fails individually and the engine cheerfully retries each one — making the flap worse. A circuit breaker would observe "the last K entries to this target failed" and *short-circuit* the rest, draining them quickly to a failed state without retry pressure on the sick downstream.

The shape that fits Edict: an `IOutboxEffectExecutor` decorator that maintains per-`(effectKind, targetKey)` Polly-style breaker state (closed → open after K consecutive failures → half-open after T cooldown → closed on success). The framework already has the executor seam; this is a small piece of code, but a regulator-visible piece of operational behaviour.

**Shape of the fix.** Ship a `EdictCircuitBreakerOptions` on `EdictOptions` that wraps every executor with a per-key breaker. Default to "off" so the existing per-entry retry stays the only behaviour for non-trading consumers; document the on-mode for trading.

#### 3.2.7 Singleton dead-letter projection under storm

`EdictDeadLetterProjectionBuilder` is a single fixed-Guid grain (ADR 0022). Under a poison-event storm — a bad deploy at market open that dead-letters even 0.1 % of a 10 k EPS workload — that is 10 promotions/sec funnelling into one grain whose outbox now grows. The mechanism that *records* the storm is itself a bottleneck.

For trading this is a regulator-visible failure mode: incident reporting flows from the dead-letter table, and if the dead-letter table is *itself* backed up the firm cannot answer "did anything fail?" promptly.

**Shape of the fix.** Partition the dead-letter projection by `hash(failureKind, eventType) % N` — same shape as §3.2.1. The query API (`IEdictDeadLetterRepository.ListAllAsync`) fans out across N grains.

### 3.3 Medium-severity items, briefly

- **Order TTL / age-reject** (§3.1 row 8): a one-line helper on `EdictCommandHandler` — `RejectIfStale(TimeSpan)` — that reads a hypothetical `Issued` timestamp on the command. The consumer adds the field; the framework adds the check. Saves every consumer the boilerplate.
- **Kill switch** (row 9): document the pattern (a `KillSwitch` aggregate read by every validator); consider a `EdictKillSwitch` primitive grain with a fast read affordance.
- **Partitioned-stream / sharded singleton** (row 10): see §3.2.2 fix.
- **Latency floor** (row 11): documentation — the framework should be explicit that the per-event latency floor is dominated by Azure Table grain-state writes (low-double-digit ms) and that **Edict is not suitable as the hot path of an HFT system**. It is suitable for institutional flow, agency flow, post-trade workflows, risk, and reporting.
- **EOD snapshot** (row 12): genuine framework work — a "freeze" affordance on a projection, or a "snapshot to a parallel table at time T" primitive. Out of scope for an MVP, in scope for a v2.
- **Wire-format evolution** (row 13): a documented additive-vs-breaking-change policy on event payloads, possibly an `EdictEvent.SchemaVersion` field, possibly a generator-emitted versioning attribute. The audit table will outlive the code; this needs a story before it has 100M rows.
- **PII redaction** (row 14): a `[EdictRedact]` attribute on event properties that the framework substitutes with a token before persist, and an operator-owned key store to detokenise on read. Genuinely hard, but increasingly table-stakes for regulated finance.
- **Linked-command correlation** (row 15): a `ParentEventId` or `CorrelationId` property on `EdictEvent`, framework-stamped from `RequestContext` on direct grain calls and from the parent event's `EventId` on stream hops. Trace context already carries this; promoting it to a queryable persisted field is the gap.

---

## 4. Performance bound by Azure-specific provider choices

A significant chunk of §3's "Edict gaps" are not really framework gaps — they are **Azure-specific provider gaps** that the framework inherits because `Edict.Azure` is currently the only provider package. Orleans is provider-agnostic by design; Edict is too at the assembly level (ADR 0014 keeps `Azure.*` deps out of `Edict.Core`); the *implementations* are not. A trading consumer hitting a latency or throughput wall is often hitting an Azure Table or Azure Queue limit, not an Edict design limit.

Naming the limits is half the work; the other half is showing where alternative Orleans-supported providers would relieve them.

### 4.1 The Azure-specific limits trading workloads actually hit

| Substrate | Limit | Where it bites in a trading flow | Workaround inside Edict today |
|---|---|---|---|
| **Azure Table grain storage** | ~5–20 ms p99 per `WriteStateAsync` (depends on partition warmth and storage account tier); 2 000 TPS per partition; 20 000 TPS per account; entity group transactions ≤ 100 entities | Every command pays at least one grain-state write; every drained outbox entry pays one ack-write. At 10 k commands/sec each raising 3 events, the storage account is the bottleneck before CPU is | The unified envelope (ADR 0018) collapses the per-command count to one; ADR 0025 moves the grain document to Blob to dodge per-property caps; per-ack writes are still O(N) per drain |
| **Azure Blob grain storage** (ADR 0025) | ~10–30 ms p99 per block-blob write; no per-property cap; per-blob throughput soft cap ~500 req/s | Lower TPS ceiling than Table for many small aggregates; *higher* room for large aggregate documents | The blob substrate is the correct call for the outbox-grows-during-burst case but trades a per-property cap for a per-blob TPS cap |
| **Azure Queue Storage streams** | 32 KB raw queue-body cap; ~100–500 ms end-to-end delivery latency under load; 2 000 TPS per queue; visibility-timeout-based delivery (not push) | Order intake → fill notification round-trip on a single Edict deploy is **dominated by queue latency**, not by handler work | Claim-check (ADR 0024) addresses the size cap; nothing addresses the latency floor |
| **Azure Blob (claim-check store)** | ~20–50 ms p99 per put; per-account egress caps under burst | Every oversized event pays a blob put on the publish side and a blob get on every consumer's receive side | Claim-check is conditional, so small events do not pay; trading messages tend to be small (FpML is the outlier, FIX-equivalent payloads are <1 KB) |
| **Azure Table reminder service** | 1-minute floor on reminder period; one cluster-wide table; one row per registered reminder | A fleet-wide downstream outage registers a reminder per dirty grain; 100 k dirty grains = 100 k rows churning at 1 Hz fleet-rate. Recovery latency for the *last* dirty grain is therefore ≥ 1 minute | None — the 1-minute floor is in Orleans's reminder service, not Edict |
| **Azure Table dead-letter / projection storage** | Same 32 KB per-property cap on the dead-letter row and on `EdictTableProjectionBuilder` row writes; partition-key throttling under hot keys | The dead-letter table is one partition (`"deadletter"`) — under a storm, partition-level TPS is the ceiling | Dead-letter row stores claim-check pointer not body (ADR 0024); §3.2.7 still flags the singleton-partition cost |
| **Azure Table query model** | No secondary indexes; `(PartitionKey, RowKey)` only; cross-partition scans are slow and cost-uncapped | Regulator queries that scan by `traderId` across all orders pay a full-table scan unless the consumer pre-built a secondary projection | Secondary-projection pattern (§4 below) is the workaround; the framework gives no guidance |

The trading-system-visible consequence: **end-to-end "command → stream → handler → ack" latency on Edict-on-Azure is 50–150 ms p99**, dominated by two grain-state writes and one queue round-trip. That is fine for institutional flow, for risk recalculation, for STP plumbing. It is fine for the OMS-internal accept/route hop. It is **not** fine if a consumer is trying to put Edict on the path of an HFT matching engine, and the framework should be explicit about that.

### 4.2 Latency / throughput envelope by provider mix

A quick comparison of the realistic envelope for "one Edict hop" (grain write + stream publish + handler dispatch + ack write) under different Orleans-supported provider mixes. All figures are order-of-magnitude steady-state on a healthy cluster, not lab-best.

| Provider mix | Grain-state write p99 | Stream hop p99 | Sustainable per-aggregate TPS | Notes |
|---|---|---|---|---|
| Azure Table + Azure Queue (today) | 5–20 ms | 100–500 ms | 50–200 | The current `Edict.Azure` envelope |
| Azure Blob + Azure Queue | 10–30 ms | 100–500 ms | 30–150 | The ADR-0025 envelope; trades TPS for document size |
| ADO.NET (Postgres) + Azure Event Hubs | 1–3 ms | 10–50 ms | 500–2 000 | Transactional grain state; push-based, partitioned, higher-throughput stream |
| ADO.NET (Postgres) + Kafka | 1–3 ms | 5–20 ms | 1 000–5 000 | The high-throughput trading-bus shape; durable ordered partitions; long retention native |
| Redis + Kafka | 0.2–1 ms | 5–20 ms | 5 000–20 000 | Sub-ms grain writes; appropriate for hot aggregates with separate durability layer; **loses single-write atomicity unless Redis is configured for AOF-everysec and the consumer accepts a 1-second durability window** |
| DynamoDB + Kinesis | 5–10 ms | 50–200 ms | 200–1 000 | AWS-native equivalent of the Azure mix; comparable envelope |

The right-hand columns are not free improvements — every one of them moves a constraint somewhere. **Redis grain state with Kafka streams** is the lowest-latency mix Orleans supports, but Redis grain state means the single-write atomicity that ADR 0018's outbox is built on now depends on Redis's persistence configuration; if a consumer points Edict at a Redis configured for "noeviction + AOF everysec" they get the atomicity, but at "allkeys-lru + AOF no" they have given it up without realising. The framework should not let the consumer make that mistake silently.

### 4.3 Where the trading-grade wins live

Of the §3 high-severity gaps, **how many evaporate under a different provider mix?**

| §3 gap | Azure-specific? | Postgres-grain + Kafka-stream impact |
|---|---|---|
| §3.2.2 singleton single-thread bottleneck | No — Orleans turn discipline, not Azure | No change |
| §3.2.3 no monotonic seq | No — framework design choice | No change |
| §3.2.4 `OccurredAt` is wire time | No — `PublishEventExecutor` design | No change |
| §3.2.5 no rate limiter | No — framework gap | No change |
| §3.2.6 no circuit breaker | No — framework gap | No change |
| §3.2.7 singleton dead-letter under storm | Partly — the partition-key throttling on Azure Table makes it worse | Postgres backing dead-letter would partition trivially; the singleton-grain-turn bottleneck remains |
| §3.1 row 11 latency floor | **Yes, almost entirely** | Drops from 50–150 ms p99 to 5–20 ms p99 |
| §3.1 row 12 EOD snapshot | Yes — Azure Table has no native snapshot; Postgres has `pg_dump --snapshot` and Kafka has compacted-topic snapshots | Achievable on Postgres without a custom framework primitive |
| §3.1 row 7 dead-letter under storm (Azure facet) | Yes | Postgres dead-letter table partitions trivially; Kafka dead-letter topic partitions natively |

The trading-grade story is therefore *partly* a framework story (Tier 1/2 of §5) and *partly* a provider-package story. **Roughly half of the latency-and-throughput pain a trading consumer will hit on Edict today comes from Azure provider limits, not from Edict's own design.** The framework's response should be to broaden the provider catalogue, not to keep working around Azure inside `Edict.Core`.

---

## 5. Integration points Edict should offer

Orleans's seam set is broader than Edict's current provider package admits. Below is the catalogue of integration points where Edict has — or could have — a substitution seam, mapped to the Orleans-supported backends a trading consumer would realistically pick.

### 5.1 Integration-point catalogue

| Edict seam | What it does | Today | Orleans-supported alternatives worth a provider package | Trading priority |
|---|---|---|---|---|
| **Grain storage** (the unified envelope's home) | Holds `GrainEnvelope { Payload, Outbox, Idempotency }`; one atomic write per command | Azure Table (small aggregates), Azure Blob (ADR 0025, for outbox-growth headroom) | ADO.NET (Postgres / SQL Server), Redis, DynamoDB, Cosmos DB | **Highest** — drops the p99 latency floor by an order of magnitude on Postgres / Redis |
| **Stream provider** (event hop) | Carries every `EdictEvent` between aggregate and consumers; the framework expects per-key implicit subscription | Azure Queue Storage | Azure Event Hubs (Microsoft.Orleans.Streaming.EventHubs), Kafka (community: Orleans.Streams.Kafka.Confluent), AWS SQS, AWS Kinesis, NATS (community) | **Highest** — Kafka in particular is the canonical trading-bus substrate; durable, partitioned, native long retention, native compacted-topic snapshots, ecosystem of downstream tools |
| **Reminder service** | Lazy crash-recovery net for the outbox; cluster-wide table | Azure Table | ADO.NET (Postgres / SQL Server), DynamoDB | **Medium** — Postgres reminders partition by primary key, removing the cluster-wide-table contention |
| **Clustering / membership** | Silo membership; orthogonal to the data path but operationally relevant | Azure Table | ADO.NET, Consul, ZooKeeper, Redis, Kubernetes, DynamoDB | **Low** — operator choice, no trading-specific pressure |
| **Claim-check store** | Oversize event body; append-only blobs | Azure Blob (`IEdictClaimCheckStore`) | S3 (trivial; same API shape), Kafka compacted topic (durability + retention native), Postgres `bytea`/large-object (transactional with the row write) | **Medium** — Kafka claim-check would let trading shops keep retention policy in one place |
| **Table projection write store** (`IEdictTableWriteStore`) | The dumb `Upsert(pk, rk, row)` seam projection builders write through | Azure Table | Postgres (real SQL queryability!), Cosmos DB, DynamoDB, Redis (for hot read-side caches), MongoDB | **Highest** — regulator queries on Postgres are SQL; on Azure Table they are a custom paginated scan |
| **Table repository read store** (`IEdictTableRepository`) | The application's read seam onto a projection | Azure Table | Same as above, plus *read replicas* (Postgres read-replica, Cosmos secondary region, Redis read-replica) | **High** — read replicas + projection write store give a regulator-query path that doesn't compete with the trading hot path |
| **Dead-letter repository** (`IEdictDeadLetterRepository`) | Forensic read of permanently-failed effects | Azure Table | Postgres (real SQL queries on the dead-letter set); the dead-letter projection is just a Table Projection so this rides on the previous seam | **Medium** — falls out of the projection-store work |
| **Idempotency window storage** | Bounded per-grain ring of handled `EventId`s; today persisted *inside* the grain envelope | Inside grain state (so wherever grain storage lives) | N/A as a separate seam; follows grain storage | — |

### 5.2 Highest-leverage integrations for a trading consumer

If the framework could pick four provider packages to ship beyond Azure, the trading-grade list is unambiguous:

| # | Provider package | Edict assembly name (suggested) | Seams it lights up | Trading-value summary |
|---|---|---|---|---|
| 1 | **Kafka stream provider** | `Edict.Kafka` | Stream provider, claim-check (compacted topic), optional dead-letter topic | Durable partitioned bus with **native long retention** — the audit-trail story stops needing a Table Projection at all for some compliance regimes; replay from Kafka is the regulator-recognised pattern; Kafka Connect ecosystem feeds downstream analytics / data lake without a custom projection |
| 2 | **Postgres provider** | `Edict.Postgres` | Grain storage, reminder service, table projection write+read store, dead-letter repository | **SQL queryability** on the audit table; transactional grain state with low p99; the trading shop's ops team already knows how to operate Postgres; HA via streaming replication is well-understood |
| 3 | **Redis provider** | `Edict.Redis` | Grain storage (hot aggregates), read-side projection cache, optional rate-limiter backing | Sub-ms grain writes for hot order aggregates; perfectly suited to backing the missing `EdictRateLimiter` primitive (Redis is the canonical token-bucket store); position-screen read cache |
| 4 | **Event Hubs stream provider** | `Edict.EventHubs` | Stream provider | The Azure-native answer to Kafka for Azure-bound consumers who cannot run Kafka — higher throughput than Azure Queues, partitioned, ~10 ms p99 latency |

Two more worth shipping for breadth but lower trading priority:

- **`Edict.Dynamo`** — AWS-bound consumers; grain storage + reminder + projection store.
- **`Edict.Cosmos`** — multi-region-write consumers (global trading shops with regional silos serving regional clients).

### 5.3 Provider-package design constraints the framework should impose

The current `Edict.Azure` package has a few design choices that should be **codified into a contract** before more providers ship, otherwise the second and third provider packages will quietly diverge.

1. **Atomic unified-envelope write is the load-bearing invariant.** Any grain-storage provider that cannot guarantee single-document atomicity (Redis without persistence configured for AOF, Cassandra LWT under contention, eventually-consistent stores without conditional writes) **breaks the outbox**. The framework should expose an `IEdictGrainStorageCapabilities` interface that declares "this provider gives single-write atomicity" and refuse to start if it doesn't.
2. **Claim-check stores must be append-only-from-Edict.** The framework deletes nothing; the operator's lifecycle policy retains. Every claim-check provider must support a "set TTL via storage policy, not via runtime delete" mode.
3. **Stream providers must preserve `EdictEvent` trace fields across the hop.** Today's Azure Queue provider does; Kafka headers naturally carry them; SQS message attributes can. The framework should ship a conformance test (the trace-stitch assertion from `Edict.Azure.Tests`) every provider package runs.
4. **Per-aggregate ordering is preserved on the happy path** — see ADR 0026. Kafka partition-by-key gives this for free; SQS does not (FIFO queues do, standard queues don't). A provider package that cannot offer per-partition ordering must opt out of the happy-path-ordering claim in its docs.
5. **Table projection write stores need a "WORM-compatible" mode** for audit projections — append-only at the table level, no `UpdateRow`, no `DeleteRow`. Azure Table supports this via immutability policy; Postgres needs an explicit table-level revoke. The framework should expose this as a per-projection option, validated against the provider's capabilities.

These five constraints turn "any Orleans provider works" into "any Orleans provider that meets the Edict capability contract works", which is the right place for the line to be.

---

## 6. The audit-trail design pattern within today's Edict — concrete answer to "global or per-aggregate stream?"

The user's question deserves a direct answer rather than burying it in the gap list.

**Per-aggregate** is the right shape on Edict today. **Global is the wrong shape on Edict today**, and asking "should we use a global stream?" is the question that flushes out the singleton-bottleneck failure mode (§3.2.2).

Concretely, on today's framework:

1. Define a domain stream per business surface (`"Orders"`, `"Trades"`, `"Positions"`).
2. Subscribe an `EdictTableProjectionBuilder<OrderAuditRow>` per aggregate Guid. PartitionKey = `orderId`; RowKey = `{eventTick:D20}-{eventId:N}`; row carries the regulator's field set.
3. For queries that *need* to scan across aggregates (a regulator asking "every order from this trader on Tuesday"), maintain a *second* projection sharded by trader: `EdictTableProjectionBuilder<TraderDailyAuditRow>` with PartitionKey = `{traderId}-{yyyyMMdd}`, RowKey = `{eventTick:D20}-{eventId:N}`. This is the "secondary index" shape every Cosmos/Table audit system converges on.
4. *Avoid* a singleton "global audit log" grain. The dead-letter projection is already one and is already a known throughput risk (§3.2.7); a second singleton is the same mistake.
5. Accept the inline-payload-only atomicity caveat: if claim-check is in play (oversize events), the audit row write can dead-letter even though the dedup window committed. Operationally that means the audit completeness SLO is "audit table + dead-letter table together are complete", not "audit table alone is complete". This needs to be documented to compliance.

The framework gap §3.2.1 names is the absence of an `EdictAuditProjectionBuilder` base that codifies steps 2–3 with a known-good partition scheme. Until that ships, every Edict-on-trading consumer writes it from scratch and some will write it wrong (singleton temptation; bad partition scheme; forgetting the claim-check caveat).

---

## 7. Summary — what Edict needs for a trading-grade story

Three tiers, now split between **framework work** (changes to `Edict.Core` / `Edict.Contracts` / `Edict.Telemetry`) and **provider-package work** (new assemblies parallel to `Edict.Azure`).

**Tier 1 — must close to be defensible at a regulator's table:**

1. **`OccurredAt` becomes intent time** (§3.2.4), stamped in `Raise`. Framework. Correctness fix, small change.
2. **Document `Edict` is not the HFT hot path** (§3.1 row 11, §4.1) — explicit latency floor in docs, with the envelope table from §4.2.
3. **Partition the dead-letter projection** (§3.2.7) — the storm-recording mechanism cannot itself be the storm. Framework.
4. **Ship `EdictAuditProjectionBuilder<T>`** (§3.2.1) — opinionated audit projection with a sharded partition scheme and a documented atomicity boundary for the claim-check case. Framework.
5. **Ship `Edict.Postgres` and `Edict.Kafka`** (§5.2) — the latency floor and the audit-retention story are *predominantly* Azure-provider issues, not framework-design issues. Postgres grain storage drops p99 by an order of magnitude; Kafka gives a native long-retention partitioned bus regulators already recognise.

**Tier 2 — needed for a trading consumer to not feel they are fighting the framework:**

6. **Analyzer / diagnostic on singleton consumers** (§3.2.2) — name the throughput ceiling at compile time, not at market open. Framework.
7. **Per-aggregate monotonic seq on `EdictEvent`** (§3.2.3) — `EdictSequencedEvent` base. Global seq deferred. Framework.
8. **`EdictRateLimiter` primitive** (§3.2.5) — token bucket grain base; pairs naturally with `Edict.Redis` (Tier 2 provider) as the backing store. Framework + provider.
9. **Circuit breaker on the outbox executor seam** (§3.2.6) — opt-in `EdictCircuitBreakerOptions`. Framework.
10. **Wire-format versioning policy** (§3.1 row 13, §5.3) — additive-vs-breaking policy, possibly a `SchemaVersion` field on `EdictEvent`; codify the provider-capability contract from §5.3. Framework.
11. **Ship `Edict.Redis` and `Edict.EventHubs`** (§5.2) — Redis for hot aggregates and rate-limiter backing; Event Hubs for Azure-bound shops that cannot adopt Kafka.

**Tier 3 — real work, real value, deferred:**

12. **PII redaction primitive** (§3.1 row 14) — `[EdictRedact]` + a tokenisation seam. Framework.
13. **Linked-command correlation as a persisted field** (§3.1 row 15) — `ParentEventId` / `CorrelationId`. Framework.
14. **EOD consistent snapshot** (§3.1 row 12) — a real framework affordance for "freeze and read at T"; sits much more naturally on Postgres (`pg_dump --snapshot`) than on Azure Table. Framework + leans on Tier 1 provider work.
15. **HLC for global ordering** (§3.2.3 continuation) — only if a consumer's regulator forces global seq. Framework.
16. **Ship `Edict.Dynamo` and `Edict.Cosmos`** (§5.2) — AWS-bound and multi-region-write shops.

None of Tier 1 or Tier 2 changes the framework's architecture. The shape (CQRS, atomic envelope, outbox, idempotent consumers, dead-letter forensic, claim-check escape hatch) is *already* the shape a trading system wants. The gaps are about meeting a regulator-facing consumer halfway: opinionated audit infrastructure, time-stamping correctness, throughput diagnostics, a small set of utility primitives (rate limit, circuit breaker, sequence number), and — perhaps most importantly — a wider provider catalogue so the trading consumer can pick the substrate that matches their latency budget instead of fighting Azure Table.

The risk of *not* closing Tier 1 is not "the framework runs slow" — it is "a regulator's inquiry surfaces an `OccurredAt` that does not match the trade ticket, and the firm cannot defend the reporting pipeline." That is a regulator-fatal outcome the framework can prevent for the cost of a `Raise`-time timestamp stamp, a sharded dead-letter table, and a Postgres provider package.
