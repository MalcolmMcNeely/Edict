# Edict brand naming: consumer-facing surface is `Edict`-prefixed, internals stay bare

**Status:** superseded by [ADR 0017](0017-drop-grain-suffix-and-refine-brand-rule.md) — originally superseded the "no prefix" decision asserted in ADR 0004's and ADR 0008's prose and in CLAUDE.md ("Base classes are named `Event` and `Command`, no prefix"). ADR 0017 drops the `Grain` suffix from the base names listed below and adds clause (b) (shared inheritance roots) to the brand rule.

Edict is treated as a brand. A type carries the **`Edict` prefix if and only if a consumer types it** — derives from it, applies it as an attribute, or receives/returns it. Infrastructure a consumer never names stays unprefixed and descriptively named, so the prefix keeps signalling "this is your contract with the framework" rather than degrading to noise on every type. The original decision deliberately chose bare `Command`/`Event` with `EdictCommand`/`EdictEvent` as the *explicitly rejected* alternative; that is reversed now, while there are no external consumers and the churn is cheap.

Branded: `EdictCommand`, `EdictEvent`, `EdictCommandResult`, `EdictRejectionReason`, `[EdictRouteKey]`, `[EdictStream]`, `[EdictTelemeterized]`, `EdictCommandHandlerGrain`, `EdictEventDeduplicationGrain`, `EdictProjectionBuilderGrain`, `EdictTableProjectionBuilderGrain`, `IEdictSender`, `IEdictTableRepository`. Bare: the sender implementation, command-route + resolver, dedup state, the unroutable-command exception, validation keys.

## Considered Options

- **Keep bare `Command`/`Event`** — rejected: bare names collide conceptually with consumers' own types and BCL names; the brand is the disambiguation, and the cost of switching is only ever lower than it is today.
- **Prefix the entire `Edict.*` surface incl. internals** — rejected: the prefix then signals nothing (it is on everything) and inflicts churn for zero consumer benefit.
- **Namespace-only disambiguation** — rejected: relies on consumers aliasing on collision; the brand should be visible at every use site, not only in `using`s.

## Consequences

- Wire identity is **unaffected**: ADR 0010 keys the manifest on the *concrete* command's simple class name via generated `[Alias(nameof(TheClass))]`, not the abstract base type name. Renaming the base does not move any serialized contract.
- The ADR-0005 generator/analyzer FQN match strings change (`Edict.Contracts.Commands.Command` → `…EdictCommand`, etc.); these live in one linked `EdictWellKnownNames.cs` (ADR 0014) so the rename is single-source.
- CONTEXT.md, CLAUDE.md, ADR 0004/0008 prose, and the auto-memory referencing bare names become stale and are updated in the same change.
- An architecture test enforces the rule: consumer-facing contract types must be `Edict`-prefixed; the prefix is the brand boundary, not decoration.
