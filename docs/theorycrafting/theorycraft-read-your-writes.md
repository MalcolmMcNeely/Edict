# Theorycraft — read-your-writes cursor

**Status:** pre-design / theorycraft. Not a PRD, not a spike, not an ADR. The goal of this doc is that a fresh session (Claude or human) can pick it up cold and start a design pass without re-deriving the problem statement.

## The problem

In CQRS, the write side (command) and the read side (projection) are separated by an async hop:

1. User clicks "Place order"
2. Command handler runs, raises an event
3. Event flows through the outbox → substrate stream → projection builder
4. Projection writes the read-model row
5. User's next page load — "show me my orders" — queries the projection

Between steps 2 and 4 there is a window — usually short, sometimes not — where the user has placed the order, the command says "Accepted," but the projection does not yet show it. The familiar "I just placed an order and it's not there."

## What every team without this primitive does

All of these are bad:

- **Poll-and-retry on the client.** UI shows a spinner and re-queries until the row appears. Wastes bandwidth, leaves the user waiting, behaves badly on slow projections.
- **Optimistic UI.** Show the change immediately based on the command result, paper over the lag. Breaks when the command actually fails after the optimistic render.
- **Hand-tracked sequence numbers.** Client receives a "version" from the command, refuses to render any projection row older than that version. Works but every team reinvents it, and every team makes a slightly different mistake.

## What the primitive does

Two halves:

**Command side.** The command result carries a small cursor — a token identifying the event the command raised. Something like `Cursor { AggregateKey, Seq }` or `Cursor { EventId }` — the wire shape is open.

**Query side.** Projection query APIs accept an optional `after: cursor` parameter. The projection knows what events it has applied (it already tracks this for dedup — ADR-0002). If applied ≥ cursor, answer immediately. If not, wait briefly (bounded timeout) until the projection catches up, then answer. On timeout, return a clear "stale" signal — not silent stale data.

From the user's view it just works: click "Place order," click "View orders," see the order.

## Why this fits Edict specifically

- **Per-aggregate single-activation grains** give you a natural per-aggregate sequence — every `Raise()` happens on one activation, in one thread, in order. The cursor's sequence component is essentially free at the command-handler base.
- **Projections already track applied events for idempotency dedup** (see ADR-0002). The "have I applied event X" check is already a live data structure.
- **Source-generated stream wiring** means the cursor-aware query method can be a generator concern — consumers don't need to thread cursor plumbing through their handler code by hand.

What's missing is (a) plumbing the cursor onto `EdictCommandResult`, (b) a query-side `WaitUntilApplied(cursor)` primitive on the projection base, and (c) the wire shape decision.

## Open design questions

1. **Cursor wire shape.** Three plausible shapes:
   - `{ AggregateKey, Seq }` — per-aggregate sequence, naturally generated, but only meaningful within one aggregate's stream
   - `{ EventId }` — already-stable, already-routed, but requires the projection to look up an applied-events set by EventId rather than compare against a monotonic counter
   - `{ Stream, SubstrateOffset }` — substrate-aligned (Kafka offset, Postgres row LSN) but leaks substrate detail across the contracts boundary, which ADR-0007 prohibits
   The least-bad fit looks like `{ EventId }` since dedup is already keyed on it. Sequence approach is faster to compare but introduces a new wire concept.

2. **Where does the cursor surface on the command result?** `EdictCommandResult.Accepted` currently is a marker. Adding a `Cursor` field changes the contract — backwards-compat is a non-issue (pre-release, see [[edict-prerelease-no-consumers]]), but the wire-shape change still touches ADR-0007 boundaries. A new ADR is likely warranted.

3. **What is the query-side surface?** Projections today are read by direct call to a projection-row grain or a generated accessor. Options:
   - Add an optional `after` parameter on every generated query method
   - Add a separate `WaitUntilApplied(cursor, timeout)` method that callers can chain
   - A new base method on `EdictProjectionBuilder` that consumers explicitly call
   The first is most ergonomic; the third is most explicit.

4. **How does "wait briefly" actually work?** Three sub-questions:
   - **Mechanism.** Orleans grain method `await`s — the projection grain holds an in-memory wait list keyed by cursor, signals waiters as it applies events.
   - **Timeout default.** Some sensible bound (50 ms? 500 ms? configurable per call?). Long enough to bridge a fast projection, short enough not to block the UI.
   - **Timeout response.** Return the stale row with a flag, or throw a `EdictReadStaleException`, or return a `Result<T, StaleSignal>` shape. Throwing aligns with current Edict patterns but is a new exception axis.

5. **What about the projection being dead-lettered before it sees the event?** If the event lands in dead-letter (poison), the cursor will never be applied. Waiters need to time out cleanly rather than hang forever — and ideally, the framework knows "this event was dead-lettered, don't bother waiting." That requires the dead-letter projection to know about cursor waiters, which is a non-trivial coupling.

6. **Sharded / partitioned projections.** Today projections are single-grain. If a future change shards them (an unlikely move given the recent removal of "sharded dead-letter projection" from the README, but possible), the cursor wait needs to know which shard owns the wait. Defer.

## Constraints from existing decisions

- **ADR-0002 idempotency model.** Projections dedup by `EventId`. That is the natural cursor candidate — no new tracking data structure needed on the projection side.
- **ADR-0007 contracts boundary.** Anything on `EdictCommandResult` crosses the wire. Cursor wire shape needs a stable MessagePack-friendly form. No `[Union]` (per ADR-0007), so cursor is a concrete record with named fields.
- **ADR-0010 event addressing model.** Defines how events are identified — read this before locking the cursor shape, because the cursor likely *is* an event address.
- **ADR-0025 chaos / bounded reorder.** Read-your-writes interacts with reorder: if event X arrives before event X-1, "applied through X" is technically true but semantically loose. The cursor's semantics need to be "applied up to and including the event the cursor names" — not "applied at least as much as the cursor."
- **Edict.Testing.** The in-memory test app is the natural first proving ground. `EdictTestApp` already exposes probes (`GetSagaProgress`, `GetProjectionRow`). A `GetProjectionRow<T>(after: cursor)` overload is the cheapest way to validate the design before widening to production substrates.

## Substrate considerations

- **Azure Queue substrate.** Events have no native sequence; the cursor cannot be substrate-derived. The per-aggregate sequence or EventId approach is the only option. This is the constraining substrate.
- **Kafka substrate.** Topic-partition offset is a natural cursor, but it is queue-level not event-level, and tying the cursor to Kafka offsets violates ADR-0007. Use the substrate-neutral cursor and let Kafka's offset advance be the underlying mechanism.
- **Postgres substrate.** Row-level sequence (event_id ordering, or a sequence column) is natural. The substrate-neutral cursor still works on top.

The cursor needs to be substrate-blind. The projection-side wait mechanism does too. This argues for `{ EventId }` or `{ AggregateKey, Seq }` over any substrate-aligned shape.

## Failure modes to design for

1. **Projection grain crashed mid-stream.** Waiters need a deactivation hook so they don't hang on an activation that no longer exists.
2. **Event dead-lettered before projection consumes it.** Waiters time out cleanly. Ideally surface "this event will never apply" rather than just "timed out."
3. **Cursor for an event that does not exist** (client bug, replay attack). Wait times out. No need to validate up front — the timeout path covers it.
4. **Cursor older than the projection has applied.** Trivial success — answer immediately.
5. **Cursor far in the future** (e.g., client cached a cursor from a much-newer event). Timeout. Same as case 3.
6. **Projection rebuild in progress** (future-work; see "Projection rebuild from history" if/when that ships). Waiters need to know whether to wait or fail fast.

## Where this lands in the code

Rough sketch — verify against current structure before designing:

- `Edict.Contracts/EdictCommandResult.cs` — cursor field on `Accepted`. May need a new ADR for the wire change.
- `Edict.Core/Projection/EdictProjectionBuilder.cs` (or equivalent base) — new query-side `WaitUntilApplied(cursor, timeout)` and/or per-query `after:` parameter
- Source generator for projections — emit cursor-aware accessor methods if going the per-query route
- `Edict.Testing/EdictTestApp.cs` — `GetProjectionRow<T>(after: cursor)` probe overload
- New tests in the conformance battery covering: caught-up immediate, brief wait, timeout, dead-lettered event

## Initial straw-man API — illustrative only

```csharp
// Command side
EdictCommandResult result = await app.SendAsync(new PlaceOrderCommand(orderId, "REF-001"));

if (result is EdictCommandResult.Accepted accepted)
{
    EdictCursor cursor = accepted.Cursor;
    // pass cursor to next query
}

// Query side
OrderRow row = await app.GetProjectionRow<OrdersByCustomerProjection, OrderRow>(
    customerId,
    after: cursor,
    timeout: TimeSpan.FromMilliseconds(200));
```

Wire shape (straw-man — pick during design):

```csharp
public sealed record EdictCursor(Guid EventId);

// Or:
public sealed record EdictCursor(Guid AggregateKey, long Sequence);
```

## Suggested first slice

Smallest thing that proves the design:

1. Add `EdictCursor` to `Edict.Contracts` with `EventId` shape (cheapest, reuses dedup infrastructure)
2. Add cursor field to `EdictCommandResult.Accepted`
3. Add `WaitUntilApplied(cursor, timeout)` method on the projection base — in-memory wait list, signals on each `Apply`
4. Expose a `GetProjectionRow<T>(after: cursor)` probe in `Edict.Testing`
5. One conformance test covering caught-up / brief-wait / timeout

That's enough to validate the API shape against the in-memory test executor. Substrate suites pick it up once the in-memory proof passes.

If the `EventId` shape proves awkward (e.g., dedup set scales poorly for waiters), pivot to `{ AggregateKey, Seq }` after the first slice — the surface changes minimally.

## Related work elsewhere

Worth scanning before designing — these are well-trodden patterns under other names:

- **Lamport timestamps / vector clocks** — closest theoretical relative; cursor is a degenerate vector clock with one component
- **Kafka offsets as consistency tokens** — same shape, substrate-coupled
- **Cosmos DB session consistency / continuation tokens** — most consumer-shaped existing example
- **Eventually Consistent reads with "read-your-writes" in DynamoDB** — same pattern, different surface
- **Postgres `pg_current_wal_lsn()` + read-after-write coordination** — same shape on a SQL substrate

A worked example from any of these is worth fifteen minutes of reading before the design ADR.
