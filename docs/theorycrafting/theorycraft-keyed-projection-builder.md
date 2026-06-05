# Theorycraft — Keyed Projection Builder

**Status:** pre-design / theorycraft. Not a PRD, not a spike, not an ADR. Goal: a fresh session (Claude or human) can pick it up cold and start a design pass without re-deriving the problem statement.

**Sibling docs:**
- [`theorycraft-read-your-writes.md`](theorycraft-read-your-writes.md) — interacts: cursor wait-list lives per-grain for keyed PBs
- [`theorycraft-projection-claim-check.md`](theorycraft-projection-claim-check.md) — interacts: keyed PB state writes hit the same substrate ceilings; design decision needed on whether to reuse the claim-check pattern or scope keyed PB to "small, fast, by-key" use cases

## The problem

Many read-model needs are pure *"give me the record for {id}"*: a customer profile, an order summary, a session view, an account snapshot. They are addressed by exact key, never queried, never iterated, and they are routinely small.

Today, Edict ships **Table Projection Builder** for read models. Table Projection Builder is the right shape for queryable / pageable read sides — but it over-designs for the by-key case:

- The consumer pays the cost of choosing partition and row keys for a read pattern that only ever fetches by one key
- Every read crosses the substrate boundary (Azure Table query, Postgres row select), even though the data could live in the grain that already owns the key
- The framework's existing per-key activation pattern (used by command handlers and sagas) is unused — wasted Orleans semantic that exactly fits this shape

Consumers in this position today either over-design a Table Projection Builder, or hand-roll a stateful grain that subscribes to an event stream — reinventing parts of `EdictIdempotencyBase`, stream subscription, and state plumbing along the way.

## What CONTEXT.md already says

The Edict glossary (CONTEXT.md) is structured for this primitive to land cleanly:

- **Projection Builder** is the *genus*: "A grain that consumes the live Event stream and maintains a current-state read model" — no commitment to where the read model lives
- **Table Projection Builder** is one *species*: "a Projection Builder whose read model lives in an external composite-key store **instead of grain state**" — the "instead of grain state" wording presupposes a sibling species
- The avoid list under Table Projection Builder explicitly warns against "putting the *durable* read model in grain state 'to be safe'" — confirming that grain-state read models are the second, currently un-shipped, sibling

So the concept already exists in the language. What is missing is the concrete consumer-facing base, the generator support, the read repository, and a glossary entry naming the sibling species.

## What the primitive does

A `EdictKeyedProjectionBuilder<TKey, TState>` (working name — see Open Question 1) is a Projection Builder whose read model lives in framework-owned durable grain state.

- Same `HandleAsync(SomeEvent evt)` authoring pattern as Table Projection Builder
- Inside the handler, the consumer mutates `State` directly — no row-write seam, no repository
- Addressed by the projection's `[RouteKey]`, same as every other keyed Edict grain
- Implicit stream subscription, just like every other event-stream consumer
- Inherits `EdictIdempotencyBase` so redeliveries are deduplicated before `HandleAsync` runs
- Read side is by-key only: consumers fetch via a framework-supplied repository (symmetric with `IEdictTableRepository`) or directly through the grain interface

```csharp
public sealed partial class CustomerView
    : EdictKeyedProjectionBuilder<Guid, CustomerState>
{
    Task HandleAsync(OrderPlacedEvent evt)
    {
        State.LastOrderAt = evt.OccurredAt;
        State.LifetimeOrderCount += 1;
        return Task.CompletedTask;
    }
}

// Read side
CustomerState view = await keyedRepository.GetAsync<CustomerView, CustomerState>(customerId);
```

## Why this fits Edict specifically

- **The concept already exists in the glossary.** This is not vocabulary expansion — it is making concrete a species already named in CONTEXT.md.
- **Reuses existing per-key activation.** Command handlers and sagas are already grain-per-key; Keyed PB adopts the same pattern with `[RouteKey]` semantics.
- **No new infrastructure invented.** Idempotency base, stream subscription, state persistence — all reused.
- **Hot reads are free.** Active grains hold state in memory; cold reads are one state load (same as a cold command handler).
- **No query API to design.** The read API is the existing grain primitive, optionally fronted by a thin repository for symmetry with Table PB.

## Open design questions

1. **Name.** Working name is `EdictKeyedProjectionBuilder<TKey, TState>`, parallel to `EdictTableProjectionBuilder<TRow>`. Alternative considerations:
   - `EdictGrainStateProjectionBuilder` — descriptive but leaks implementation
   - `EdictSingleKeyProjectionBuilder` — accurate but clunky
   - `EdictKeyedProjectionBuilder` — picks up the existing "Table" naming style, signals "addressed by key, not by query"
   Recommend: Keyed.

2. **Read API surface.** Three plausible shapes:
   - Framework-supplied repository: `IEdictKeyedProjectionRepository` with `GetAsync<TProjection, TState>(key)`. Symmetric with `IEdictTableRepository`. Easy to swap in tests.
   - Direct grain interface: consumer calls `IClusterClient.GetGrain<ICustomerView>(id).GetStateAsync()`. Most Orleans-native, no extra surface.
   - Both: repository for tests and consistency with Table PB; grain interface for the unusual hot-path case.
   Recommend: repository, with grain interface accessible if a consumer really wants it.

3. **State storage isolation.** Aggregate state and Keyed PB state both live in the substrate's grain persistence layer:
   - Same provider, same state name → collision risk if the projection key happens to equal an aggregate key
   - Same provider, namespaced state name → safe but adds wire complexity
   - Different provider entirely → safe, independent scaling, more configuration surface
   Recommend: same provider, namespaced state name. Conformance tests assert no collision when a Keyed PB and a Command Handler share a key.

4. **Claim-check interaction.** Keyed PB state writes go through grain persistence and hit the same substrate ceilings as command-handler aggregate state (~1 MB on Azure Table, 400 KB on DynamoDB). Two options:
   - Reuse the projection-claim-check pattern for grain state — extends claim-check beyond projection rows into grain-state writes. Bigger surface change.
   - Scope Keyed PB to "small, fast, by-key" views and document the ceiling. Consumers with large views use Table PB (which gets claim-check separately).
   Recommend: scope-and-document on first ship; promote to claim-check-aware later if real consumer pain emerges.

5. **Read-your-writes cursor interaction.** The cursor wait-list (from the sibling theorycraft) lives in the grain — one wait-list per activation, in-memory, signalled as events apply. Symmetric with how a Table PB's wait-list would work, but per-grain instead of per-row-set.

6. **Cross-key visibility.** None, by design. A Keyed PB cannot answer "all customers in region X." Consumers who need that build a Table PB *as well*, subscribed to the same events. Document this hard line — it is the trade-off that earns Keyed its place.

7. **Cold-read latency.** First read of a never-activated grain costs one state load; subsequent reads are in-memory. Worth a benchmark slot before shipping, but no design action.

8. **Should Keyed PB support multiple `[RouteKey]` parameters or composite keys?** Today's `[RouteKey]` model is single-Guid. If a consumer wants `(customerId, productId)` keying, do they declare a synthetic key event, or does the framework allow composite keys here?
   Recommend: defer composite keys. Use synthetic key types in v1.

## Constraints from existing decisions

- **CONTEXT.md glossary.** Already permits the sibling species. Update needed: new glossary entry for **Keyed Projection Builder**, drop or rephrase the `_Avoid_` line under Table Projection Builder that warns against "putting the durable read model in grain state" — that warning is correct for Table PB consumers, but misleading once Keyed PB exists as a first-class species.
- **ADR-0002 idempotency model.** Keyed PB inherits `EdictIdempotencyBase` like every other consumer. The dedup-window semantic and `EventId` keying carry over unchanged.
- **ADR-0007 contracts boundary.** Keyed PB's state shape lives inside a grain — it does not cross the contracts boundary. Wire shape concerns do not apply here unless the read repository returns state directly across a remote grain call (it will, via Orleans serialization, which is MessagePack already in this codebase).
- **Substrate seam (ADR-0030).** State persistence is part of the substrate. No new seam required — Keyed PB reuses the existing grain-state persistence path.
- **Naming convention (CONTEXT.md, section "Naming convention (brand)").** Consumer subclasses are named `{Name}{Role}`. The role suffix for Keyed PBs is open: `{Name}View`? `{Name}KeyedProjection`? `{Name}ProjectionBuilder` with no further qualifier? Pick during design — `{Name}View` reads most naturally for the customer-record use case.

## Substrate considerations

- **Azure.** Grain state lives in Azure Table Storage via the existing persistence provider. ~1 MB ceiling per state row (header overhead, encoded bytes). Scope-and-document is the cleanest first-ship path.
- **Postgres.** Grain state lives in `EdictPostgresGrainStorage` (per the Postgres slice). Effectively unlimited payload size; the ceiling is not a practical concern.
- **Kafka substrate.** Kafka is streaming-only; the state side is whichever persistence substrate is paired (Azure or Postgres). Same constraints as the paired substrate apply.
- **DynamoDB** (when shipped). 400 KB grain state ceiling. Tightest of the substrates Edict is likely to target. Reinforces the case for scope-and-document on first ship.
- **MongoDB** (when shipped). 16 MB grain state ceiling via the document model. Rarely a constraint in practice.

## Failure modes to design for

1. **State write fails mid-handler.** The event has been dedup-window-committed but the state write did not land. On redelivery, idempotency suppresses the second handler call — silent data loss. This is the same trap that exists today for any consumer with side-effecting `HandleAsync`, and Edict's answer is the same: the dedup window commits *with* the state write, in one durable transaction. Keyed PB must inherit this — verify the existing `EdictIdempotencyBase` does so for grain-state consumers.

2. **Cold-read of never-activated grain.** Returns default state. Acceptable — same shape as a never-activated command handler aggregate. Repository can optionally signal "no events have applied" if the consumer wants to distinguish. Defer to design.

3. **Poison event in stream.** Dead-letter path (same as other consumers). Keyed PB's state is unchanged because the handler never ran past the throw. Idempotency dedup commits *before* the handler runs, so a poison event does not re-throw indefinitely — it gets promoted to dead-letter same as any consumer.

4. **Concurrent reads during state update.** Orleans serialises by activation. No special design needed.

5. **State migration / schema evolution.** Keyed PB state is a typed object; renaming a field breaks the wire shape of persisted state. Same problem aggregates have today; same answer (schema evolution is a separate concern, currently on the open list for the framework — see README "What's next").

## Non-goals — explicit

- **Cross-key queries.** Use a Table Projection Builder instead.
- **Pageable / iterable views.** Same.
- **Large derived artifacts (rendered PDFs, ML feature blobs).** Use a Table Projection Builder + projection-claim-check.
- **Read replay / rehydration.** Edict is event-driven, not event-sourced (CONTEXT.md line 3). Keyed PB inherits this — there is no replay-from-history path, only forward-from-subscription.

## Where this lands in the code

Rough sketch — verify against current structure before designing:

- New base class `EdictKeyedProjectionBuilder<TKey, TState>` in `Edict.Core` (alongside the existing `EdictTableProjectionBuilder<TRow>`)
- Generator support: extend whatever generator handles Table PB to recognise the new base, emit implicit stream subscription, idempotency wiring, state plumbing
- New repository interface `IEdictKeyedProjectionRepository` in `Edict.Contracts` for symmetric read-side access
- Conformance battery additions: per-substrate test that writes events through the command pipeline, reads back via repository and via grain interface, asserts parity and applied state
- Sample app addition: a `CustomerView : EdictKeyedProjectionBuilder<Guid, CustomerState>` that consumes existing sample events
- CONTEXT.md glossary update: new entry for Keyed Projection Builder; rephrase or drop the misleading `_Avoid_` line under Table Projection Builder
- New ADR documenting the species split and the rationale for two first-class Projection Builder shapes

## Suggested first slice

Smallest thing that proves the design:

1. Define `EdictKeyedProjectionBuilder<TKey, TState>` base in `Edict.Core`
2. Generator support: implicit stream subscription, idempotency, state plumbing
3. `IEdictKeyedProjectionRepository` in `Edict.Contracts`, with one implementation
4. Sample: `CustomerView` Keyed PB consuming `OrderPlacedEvent`
5. Conformance test (one substrate — Azure or Postgres, pick one): send a command, observe applied state via repository
6. CONTEXT.md update + new ADR

That is enough to validate the API shape against a real substrate. Other substrates pick it up via the existing conformance pattern. Claim-check-aware state and cursor integration land in subsequent slices.

## Related work

Worth scanning before designing — these are the well-trodden patterns under other names:

- **Akka Persistence keyed actors as read models.** Same shape on the JVM actor side.
- **Axon Framework single-aggregate views / "subscription queries."** Direct conceptual parallel.
- **EventStoreDB single-stream projection.** State-machine-per-key shape, similar trade-offs.
- **Marten "self-aggregating projection."** Document-store implementation of the same idea.
- **Orleans grain-as-view pattern** (community articles). The pattern Edict consumers currently hand-roll.

A worked example from any of these — particularly Akka Persistence and Marten, which are closest to Edict's substrate choices — is worth fifteen minutes of reading before the design ADR.
