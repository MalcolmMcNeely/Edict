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

## Design decisions — grill 2026-06-05 (supersedes the straw-man above)

A `grill-with-docs` pass against the live code overturned the straw-man's central premises. The code findings and the resulting locked decisions:

### Code findings that reshaped the design

1. **The straw-man cursor (`{ EventId }`) is wrong for chains.** `EventId` is per-hop (minted fresh in `OutboxHost.EnqueueRaisedEventsAndDrainAsync`). At the far end of a `command → event → [saga] → command → event → projection` chain, the projection applies an event whose `EventId` the original caller never saw. `EventId` only works for the single-hop case; a saga in the middle breaks it.
2. **Dedup is a bounded ring, not a high-water mark.** `IdempotencyState` is `HandledEventIds[100]` + `Head` + `Count`; `Contains` answers "in the last N handled", not "progressed past". So membership cleanly answers "wait until applied" but is unsound for "applied long ago" (an evicted id reads as not-applied) — benign here because the fallback is the plain poll-read.
3. **No correlation/causation id exists.** `EdictCommand.CommandId` is inert (nothing assigns it). Chain stitching today is W3C trace context, which is `null` when unsampled — unusable for a correctness feature.
4. **Reads are store-direct and off-grain.** Consumers read via `IEdictTableRepository<T>` straight against the store, on a tier that is a separate process from the silo. The projection grain only writes (an `UpsertRow` outbox effect draining *after* the dedup-ring commit). The grain is never on the read path. The maintainer ruled this shape *wrong*.

### Locked decisions

- **Token is a chain-stable correlation id, not an `EventId`.** Minted-if-empty in `EdictSender.SendAsync` (honours a caller-supplied value), stamped onto the dispatched command so it propagates, and echoed on `EdictCommandResult.Accepted` as an opaque `EdictCursor` (wrapper, not a bare `Guid`, so it can widen later). Term is **correlation id** (constant across the conversation), not causation id (per-hop parent).
- **Read-through-grain is the new foundation.** `IEdictTableRepository<T>` is **deleted** (no escape hatch; if query-heavy table reads bite later, solve then). Reads move onto the projection grain via a hand-written `IEdictProjectionReader<TRow>` facade (dodging the Orleans codegen-ordering trap, as `IEdictSaga` does), backed by an `[AlwaysInterleave]` grain read method that the source generator applies — mandatory, because a non-interleaving blocking wait self-deadlocks (the stream turn that would satisfy the wait queues behind it on the single activation).
- **Rename + taxonomy.** `EdictTableProjectionBuilder<T>` → `EdictListProjectionBuilder<TRow>`. `EdictProjectionBuilder` stays the abstract shared root (discovery marker for the generator/EDICT009 + the new read/wait mechanism). A future grain-state `EdictProjectionBuilder<TKey,TState>` (out of scope, per `docs/projection-builder-naming.md`) slots in as a concrete sibling under the same root and inherits the identical read/wait seam. **Consumer subclasses are `{Name}ProjectionBuilder`** (storage-neutral, role-suffixed — matches the brand `{Name}{Role}` convention; the base type disambiguates the species). The rename is **not standalone**: it folds into this slice (it re-touches the same files the repo-deletion + grain-read changes do — including the MCP `HandlerRole.TableProjectionBuilder` enum value, its metadata-name constant in `HandlerScanner`, and 3 `.verified.txt` snapshots), each file touched once. Historical ADRs (0011/0013/0032) are left as point-in-time records, not rewritten.
- **Read result is a typed tri-state, never a throw.** `EdictProjectionRead<TRow>(TRow? Row, EdictReadStatus Status)` with `Status ∈ { Immediate, CursorReached, CursorTimedOut }`. Throwing was rejected: CLAUDE.md's exception philosophy reserves throws for framework faults; eventual-consistency lag is an *expected* outcome and belongs in a typed result. (`OperationCanceledException` on caller cancellation is the one legitimate throw.)
- **Three read modes via `(after, timeout)` on one seam.** `after: null` → `Immediate` (poll). `after: cursor` + `timeout` → bounded wait (`CursorReached`/`CursorTimedOut`). `after: cursor` + explicit `Timeout.InfiniteTimeSpan` → indefinite wait (`CursorReached` only). **Indefinite must be explicit** — an omitted timeout on a cursor read falls back to a bounded `EdictOptions` default, never infinite, so a forgotten timeout can't hang a request. Mirrors the `EdictSchedule.Unbounded` explicit-opt-out idiom. Indefinite is safe because an in-flight grain call pins the activation, so the in-memory wait-list survives.
- **Projection remembers the last X processed correlation ids** in a bounded, persisted ring (mirroring `IdempotencyWindowSize`, tunable), so grain growth stays bounded and a post-reactivation read can still answer "already processed". Eviction → benign fallback to poll.
- **Propagation rule (verified across the saga hop):** a message inherits the correlation of the message that caused it; an origin with none mints one. Three stamp points — `EdictSender.SendAsync` (mint-if-empty, honours caller-supplied), command→raised-events (at the `EventId`-mint in `EnqueueRaisedEventsAndDrainAsync`, from the command in hand at `ValidateAndHandleAsync`), saga event→dispatched-command (`EdictSaga.DispatchEventAsync`, from the handled event before `BuildSendCommandEntry`). A timer/schedule fire has no upstream message, so it naturally starts a fresh correlation via `SendAsync`'s mint-if-empty — correct (a time-trigger is a new causal root). Requires a new `CorrelationId` field on **both** `EdictCommand` and `EdictEvent` (wire change). Bonus: dead-letter rows can group by correlation.
- **`CursorReached` = "any/first-applied", documented as such.** When one correlation produces multiple events to the *same* projection, the marker is recorded on the first applied (all-applied is unknowable without an event-count wire concept that fights ADR-0010's dynamic fan-out). Honest contract: *`CursorReached` means at least the correlation's first effect on this projection is visible.* Multi-`Raise` (multiple events per command, hence per correlation) is verified supported (batch-publish #160), so this is real, not hypothetical. Mitigation is **guidance, not enforcement**: a docs/skill recommendation to prefer one event per command (no analyzer — enforcing it would break batch-publish).
- **Processed-correlation ring: persisted, projection-only envelope slot, separate tunable.** New `GrainEnvelope` slot `CorrelationProgress? [Id(5)]` (null for non-projection roles, exactly the `Saga[3]` pattern; highest current ordinal is `[Id(4)]`), bounded ring shape mirroring `IdempotencyState` (`Guid[]` + head + count). Persisted so read-your-writes survives a projection deactivate/reactivate; the advance rides the existing dedup-ring commit, so no extra write. Tunable `EdictOptions.CorrelationWindowSize` (default 100, distinct from `IdempotencyWindowSize`). Extending `IdempotencyState` was rejected — it is shared by all idempotent consumers and would allocate the ring for grains that never read-your-write.
- **The processed-correlation marker ties to store-visibility, not ring-commit.** The *persisted* ring advances in the main commit (pre-drain), but the *live* "is C processed" check reads an **in-memory mirror updated post-drain** (mirroring the existing `DedupRingMirror`). A read interleaving between commit and drain doesn't see C yet (correct — row not in store), registers as a waiter, and is signalled at end-of-turn once the `UpsertRow` landed. The persisted ring is consulted only at activation, where drain-on-activation has already made the store consistent. Net: correct store-visibility with zero extra hot-path writes. On the crash path the marker correctly lags until the recovery drain writes the row.

- **`EdictCursor` wire shape.** `[MessagePackObject(keyAsPropertyName:true)] readonly record struct EdictCursor(Guid CorrelationId)` — allocation-free, `EdictCursor?` as the `after` param, public ctor so a caller-supplied correlation can build one without round-tripping `Accepted`. Echoed on `Accepted`: `sealed record Accepted(EdictCursor Cursor)` (was parameterless → wire change). Wrapper not bare `Guid`, for type safety + frozen-surface flexibility.
- **Dead-letter reads re-back, don't drop.** Deleting the generic `IEdictTableRepository<T>` removes the backing of `IEdictDeadLetterRepository`. Keep that named operator forensic facade (distinct operator surface; preserves the CONTEXT.md guidance) but re-back it on the grain read (`IEdictProjectionReader<EdictDeadLetterEntry>` against the singleton key). Only the generic repository dies.

### Surface requirements (EDICT-SURFACES walk, 2026-06-05)

1. **CONTEXT.md** — lands with the implementation slice (not before, to avoid glossary/code drift): rename the "Table Projection Builder" term → "List Projection Builder"; replace "Table Repository" with "Projection Reader"; add "Correlation Id", "EdictCursor", "Projection Read" (tri-state); amend the "Event" `_Avoid_` line (an explicit chain-stable correlation id now exists, distinct from trace context).
2. **ADRs — two.** (a) *Projection reads through the grain* — read-through-grain, `[AlwaysInterleave]`, `IEdictProjectionReader<TRow>`, delete `IEdictTableRepository<T>`, the `EdictListProjectionBuilder`/`EdictProjectionBuilder` taxonomy; supersedes/amends ADR-0011, 0013, 0032, folds in `projection-builder-naming.md`. (b) *Read-your-writes via correlation cursor* — correlation primitive + propagation, `EdictCursor` on `Accepted`, the `EdictProjectionRead` tri-state, the bounded persisted ring; rationale for correlation over EventId/trace. Next free numbers ≈0057/0058. Historical ADRs 0011/0013/0032 are left intact (superseded, not rewritten).
3. **Skills** — `edict-authoring` (read-your-writes read pattern + `{Name}ProjectionBuilder` species); `edict-testing` (probe is a flow assertion, no cursor seam); `edict-diagnostics` (`CursorTimedOut` is lag, not a fault); `edict-contracts` light note (correlation id is framework-stamped, not consumer-authored).
4. **MCP** — no new tool. `HandlerRole.TableProjectionBuilder` → `ListProjectionBuilder` + the `HandlerScanner` metadata-name constant; regenerate 3 `.verified.txt` snapshots.
5. **Usage docs** — new first-class concept page `docs/usage/concepts/read-your-writes.md`; update `projection-builders.md` / `table-projections.md` (rename + read-through-grain); touch `dead-letter.md` (re-backed forensic facade).
6. **Drift guards** — AgenticTooling interlock re-checks after the role rename (no new assertion expected); `ConfigurationDocCoverage` picks up the new `CorrelationWindowSize` + read-timeout-default options.
7. **Sample** — migrate both webs' `IEdictTableRepository<T>` reads → `IEdictProjectionReader<TRow>`. **Hub.razor (the "traffic" simulator) stays the poll demo** (`after: null`, its existing `PeriodicTimer`). **Playground.razor becomes the cursor demo** — each `Sender.SendAsync` feeds `Accepted.Cursor` into a cursor read, dropping its poll timer. The poll-vs-cursor contrast is the sales pitch; both share `Sample.Web.Components`.
8. **Wire shape** — `EdictCommand.CorrelationId`, `EdictEvent.CorrelationId`, `Accepted(EdictCursor Cursor)`, `EdictCursor`, `GrainEnvelope.CorrelationProgress [Id(5)]`. Regenerate `CommandWireShapeTests` + contract round-trip Verify; `OutboxEffectKindFrozenOrdinalTests` untouched (no new effect kind).
9. **Edict.Testing** — *no cursor/timeout seam* (consumer-only, flow assertion). `GetProjectionRow` stays a store-reading probe (uses the surviving internal `IEdictTableStoreFactory`). The cursor mechanism (wait-list, timeout against the injected `TimeProvider`, `CursorReached`/`CursorTimedOut`, ring eviction, propagation, any-semantics) is tested in `Edict.Core.Tests`.
10. **Conformance** — one **persistence-axis** scenario: send command → capture `EdictCursor` → force projection reactivation → cursor read → assert `CursorReached` + row reflects the write. Binds Azure Table + Postgres (exercises grain-state persistence + real store round-trip / store-visibility). Streaming axis adds nothing. Timeout/eviction/any-semantics stay Core unit tests (pure in-grain, no backend dependency) — a recorded skip.

### Suggested first slice (revised from the straw-man)

The read-through-grain ADR (2a) is the foundation and ships first as its own slice (rename + `IEdictProjectionReader` + `[AlwaysInterleave]` gen + repo deletion + dead-letter re-back + Sample migration), green before the correlation work rides on top. Then the correlation-cursor slice (2b): the two new wire fields + propagation stamps + `EdictCursor` on `Accepted` + the persisted ring + the typed read result + the one conformance scenario + the Playground cursor demo.

## Related work elsewhere

Worth scanning before designing — these are well-trodden patterns under other names:

- **Lamport timestamps / vector clocks** — closest theoretical relative; cursor is a degenerate vector clock with one component
- **Kafka offsets as consistency tokens** — same shape, substrate-coupled
- **Cosmos DB session consistency / continuation tokens** — most consumer-shaped existing example
- **Eventually Consistent reads with "read-your-writes" in DynamoDB** — same pattern, different surface
- **Postgres `pg_current_wal_lsn()` + read-after-write coordination** — same shape on a SQL substrate

A worked example from any of these is worth fifteen minutes of reading before the design ADR.
