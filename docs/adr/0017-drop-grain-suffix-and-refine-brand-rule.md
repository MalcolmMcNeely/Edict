# Drop the "Grain" suffix from Edict's surface; refine the brand rule and freeze persisted-state aliases

**Status:** accepted — supersedes [ADR 0013](0013-edict-brand-naming-convention.md); updates the type names referenced in ADR 0014 and ADR 0015 prose

"Grain" is an Orleans implementation noun. Edict's entire brand premise (ADR 0013) is that a consumer never names a grain — so carrying `Grain` on the base classes a consumer derives from is exactly the leak the brand prefix exists to prevent. We therefore drop `Grain` from **both** Edict's own grain base classes and the consumer subclasses that derive from them, refine the brand rule to cover shared inheritance roots, and adopt a frozen-alias rule for persisted serializable state.

## Decisions

1. **No `Grain` suffix.** Base classes:
   - `EdictCommandHandlerGrain` → `EdictCommandHandler`
   - `EdictProjectionBuilderGrain` → `EdictProjectionBuilder`
   - `EdictTableProjectionBuilderGrain` → `EdictTableProjectionBuilder`
   - the dedup inheritance root (`EdictEventDeduplicationGrain` in ADR 0013 prose; `EdictEventIdempotentGrain` as the code had drifted) → **`EdictIdempotencyBase`**
2. **Consumer subclasses are `{Name}{Role}`** — `OrderCommandHandler`, `OrdersByStatusProjectionBuilder`, and (future) `{Name}EventHandler`, `{Name}Saga`. Never `{Name}Grain`.
3. **Refined brand rule.** A type is `Edict`-prefixed if and only if **(a)** a consumer types it (derives from it, applies it as an attribute, or receives/returns it), **or (b)** it is an inheritance root shared by the consumer-facing grain bases. `EdictIdempotencyBase` keeps the prefix under (b) even though no consumer derives from it directly — it sits in every consumer grain's base chain, so the prefix still signals "framework contract" and reads coherently in the hierarchy.
4. **Raw Orleans test doubles keep "Grain".** Fixtures that derive from Orleans `Grain`/`Grain<T>` directly (e.g. `DedupPublisherGrain`, `OrderEventCaptureGrain`) are honestly grains, not Edict abstractions or consumer examples. The convention governs Edict's surface and consumer-shaped code, not genuine Orleans infrastructure.
5. **Frozen alias for persisted serializable state.** Every `[GenerateSerializer]` type that is persisted as grain state or crosses the wire carries an `[Alias]`; `ORLEANS0010` is never suppressed. The alias on **persisted state** (e.g. `IdempotencyState`) is a **frozen string literal**, *not* `nameof`, because the state outlives deploys and must deserialize after a class rename. This is deliberately different from ADR 0010, where commands use `[Alias(nameof(TheCommand))]`: a command's wire identity is *intentionally* its concrete simple name and renaming a command is a deliberate breaking change, whereas silently losing the dedup ring on a rename is a correctness bug with no exception (ADR 0002).

## Considered Options

- **Rename consumer subclasses only, keep `Grain` on the bases** — rejected: `Grain` still leaks into every consumer's `: EdictXxxGrain` base list, defeating the brand purpose.
- **Keep the strict ADR 0013 "iff a consumer types it" rule and list `EdictIdempotencyBase` as a one-off exception** — rejected: a principled carve-out for shared inheritance roots covers future roots (EventHandler/Saga bases) without exception creep.
- **Use `[Alias(nameof(IdempotencyState))]` for the dedup state, matching ADR 0010's command pattern** — rejected: `nameof` tracks the rename, defeating rename-resilience for a type whose whole point is to survive renames across deploys.

## Consequences

- Public-surface rename touching `EdictWellKnownNames.cs` (single-source FQN constants — ADR 0014), generators, analyzers, all test/sample subclasses, every snapshot, CONTEXT.md, CLAUDE.md, and the csharp/testing skills, applied in one change.
- Wire identity is **unaffected**: ADR 0010 keys the manifest on the concrete command's simple class name, not the base type name (carried over from ADR 0013).
- ADR 0013's branded list is restated here with `Grain` removed and the (b) clause added; ADR 0013 is superseded, not edited.
- The architecture test that enforced the branded surface is updated to the new names and the (a)/(b) rule.
