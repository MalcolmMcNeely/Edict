# Public surface preserved for types in consumer base chains

A type referenced anywhere in the inheritance chain or public-ctor signature of a consumer-facing `Edict*` base class stays `public`, even when the type itself is otherwise framework-internal under ADR 0017. The C# compiler enforces this before any visibility audit reaches it.

The visibility-audit work for `Edict.Core` (PRD #196) hit two cases where the brand rule says "no consumer types it" yet flipping to `internal` is a CS error inside `Edict.Core` itself, not at the consumer's compile.

**Case 1 — base chain (CS9338).** `EdictCommandHandler<TState>` (`public`) and `EdictIdempotencyBase<TPayload>` (`public`) inherit `Grain<GrainEnvelope<TState>>`. C# requires the constructed base type's effective accessibility ≥ the derived class's. Marking `GrainEnvelope<T>` `internal` fails with **CS9338: Inconsistent accessibility: type 'GrainEnvelope<TState>' is less accessible than class 'EdictCommandHandler<TState>'** — inside `Edict.Core`. Verified with a throwaway classlib repro. `OutboxSlice` / `OutboxEntry` / `OutboxEffectKind` / `UpsertRowEffect` / `IdempotencyState` are *not* in the base chain — they're property types on the (forced-public) `GrainEnvelope` — so they could in principle go internal if `GrainEnvelope`'s slot properties go internal. PRD #196 chose not to, on the basis that splitting the envelope's public/internal surface delivers less than the brand-rule violation costs.

**Case 2 — public ctor param of consumer-typed base (CS0051 / clause (a)).** `EdictTableProjectionBuilder<T>(IEdictTableStoreFactory writeStoreFactory)` is a `public abstract` base. The consumer's projection builder takes `IEdictTableStoreFactory` as a ctor param and passes it through. Under ADR 0017 clause (a) — "a consumer types it" — `IEdictTableStoreFactory` is consumer-facing and stays `public`. Flipping to `internal` would also fire CS0051 on the base ctor before the consumer sees it.

The same shape applies for any future `Edict*` base that exposes a framework type via inheritance or ctor: that framework type is implicitly consumer-typed and stays public.

## Considered Options

- **Refactor the base chain to remove the framework type** — rejected for `GrainEnvelope`: Orleans grain state requires `Grain<TGrainState>`, and the envelope is the persisted document. Rejected for `EdictTableProjectionBuilder`: switching from ctor injection to `ServiceProvider.GetRequiredService<>()` is a consumer-surface change touching every projection builder; it belongs to a separate refactor PRD, not the visibility audit.
- **`[EditorBrowsable(Never)]` on the offending types** — partial rescue. Keeps brand-rule compliance for IntelliSense but the type still appears in compile errors and on the public binary surface. The architecture-test allow-list still has to list them. Chosen for the generator-only fast paths (`EdictSender.SendFastPathAsync`, `EdictCommandHandler.RaiseFast<TEvent>`) where the consumer never types them but the source generator needs them callable. Not chosen for `GrainEnvelope` / `IEdictTableStoreFactory` because the consumer's own code types them directly, so hiding them from IntelliSense would be misleading.
- **Make the ctor `internal` on the public class** — chosen for `EdictSender`. The class stays `public` for the interceptor's `is EdictSender` cast; the ctor goes `internal` so `CommandRouteResolver` can go `internal`. DI's `ActivatorUtilities` constructs internal-ctor classes fine. Inapplicable to `EdictTableProjectionBuilder` because consumers inherit from it and pass the param through, so an internal base ctor would break the public derived ctor.
- **Split persisted-state slot properties from the envelope** — kept as a future option. `GrainEnvelope` stays `public` with all public slots today; flipping the `Outbox` and `Idempotency` slot properties to `internal` (and their types to `internal` along with them) is a follow-up that delivers part of the brand-rule goal without breaking the base chain. Out of scope for PRD #196.

## Consequences

- `PublicSurfaceAllowListTests.EdictCore_PublicTypesMatchAllowList` lists `GrainEnvelope<TPayload>` and `IEdictTableStoreFactory` permanently. A comment on each entry points at this ADR.
- Future `Edict*` base classes must consider what they expose via inheritance and ctor. Adding a new framework type to a base chain promotes that type to consumer-facing whether intended or not.
- The brand-rule conversation at PR review (ADR 0017) checks not just the type's annotations and naming, but also whether anything `public` reaches it transitively.
- The follow-up "split GrainEnvelope slots" option remains on the table if the slot-types' public binary-compat tax outweighs the cost of an asymmetric envelope shape.
