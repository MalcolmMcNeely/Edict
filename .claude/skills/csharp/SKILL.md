---
name: csharp
description: Use this skill when editing, creating, or reviewing any C# file (.cs) in this repo. Covers naming conventions, using directive rules, brand-prefix rule, assembly boundaries, and Edict.Core concept-folder placement.
---

# C# Coding Conventions

## Naming

Never abbreviate variable, parameter, field, or property names. Always use the full word.

```csharp
// Bad
CancellationToken ct
IServiceProvider sp

// Good
CancellationToken cancellationToken
IServiceProvider serviceProvider
```

Domain acronyms that are proper nouns or file-extension identifiers are allowed. The framework entry-point parameter `string[] args` is also allowed.

## Using directives

Always resolve types with `using` directives at the top of the file. Never qualify a type with its full namespace inline (e.g. write `EdictCommand`, not `Edict.Contracts.Commands.EdictCommand`). If a name collision exists, use a `using` alias rather than inline qualification.

## Brand-prefix rule (ADR 0017, supersedes 0013)

A type is `Edict`-prefixed **iff (a)** a consumer types it — derives from it, applies it as an attribute, or receives/returns it — **or (b)** it is an inheritance root shared by the consumer-facing grain bases. The prefix keeps signalling "this is your contract with the framework."

**No `Grain` suffix.** "Grain" is an Orleans implementation noun a consumer never names. Edict abstractions and consumer subclasses never end in `Grain`. Consumer subclasses are `{Name}{Role}`: `OrderCommandHandler`, `OrdersByStatusProjectionBuilder`, future `{Name}EventHandler` / `{Name}Saga`.

**Consumer-facing (always `Edict`-prefixed):**
`EdictCommand`, `EdictEvent`, `EdictCommandResult`, `EdictRejectionReason`, `[EdictRouteKey]`, `[EdictStream]`, `[EdictTelemeterized]`, `EdictCommandHandler`, `EdictProjectionBuilder`, `EdictTableProjectionBuilder`, `IEdictSender`, `IEdictTableRepository`. Plus `EdictIdempotencyBase` under clause (b) — no consumer derives from it directly, but it is the shared root of every consumer grain base.

**Internal (stays bare):** sender implementation, command-route resolver, dedup state, exception types. If a consumer never names it and it is not a shared inheritance root, it does not carry the prefix. **Raw Orleans test doubles** that genuinely derive from `Grain`/`Grain<T>` (e.g. `DedupPublisherGrain`) keep "Grain" — they are honest grains, not Edict abstractions.

## Assembly boundaries (ADR 0014, 0015)

| Assembly | What goes here |
|---|---|
| `Edict.Contracts` | Consumer-typed surface: `EdictCommand`, `EdictEvent`, attributes, `IEdictSender`, `IEdictTableRepository`, and the internal write-store seam. No Orleans server runtime. |
| `Edict.Core` | Persistence-agnostic grain runtime: `EdictCommandHandler`, `EdictIdempotencyBase`, `EdictProjectionBuilder`, `EdictTableProjectionBuilder`. Must not depend on `Azure.*`. |
| `Edict.Telemetry` | `ActivitySource`, span extensions, ADR-0003 stream-hop trace capture (`RequestContext`). References `Orleans.Core`, not the server runtime. |
| `Edict.Azure` | All Azure-specific implementations. The only assembly that may depend on `Azure.*`. |
| `Edict.Generators` | Roslyn source generators (`netstandard2.0`). |
| `Edict.Analyzers` | Roslyn analyzers and `EDICT00x` diagnostics (`netstandard2.0`). |

### Placement decision

Ask: "Does a consumer ever name this type?"

- **Yes** → `Edict.Contracts`, `Edict`-prefixed
- **No, it's grain runtime logic** → `Edict.Core`, bare name
- **No, it's Azure-specific** → `Edict.Azure`
- **No, it's telemetry helpers** → `Edict.Telemetry`
- **It's a generator/analyzer** → `Edict.Generators` or `Edict.Analyzers`

## Edict.Core concept folders (ADR 0014)

`Edict.Core` is organised by concept, not by type:

```
Edict.Core/
  Commands/     – EdictCommandHandler, sender, routing
  Idempotency/  – EdictIdempotencyBase and IdempotencyState (shared inheritance root)
  Projections/  – EdictProjectionBuilder and EdictTableProjectionBuilder
  Sagas/        – (future) saga base
  Serialization/
  TableStorage/ – framework-internal write-store seam
```

`Idempotency/` is the shared inheritance root — it is not an Events concept and does not live under `Events/`. Add new concept sub-folders only when a clear grouping emerges.

## Other conventions

- **No redundant `private`.** Members are private by default — omit the keyword. `.editorconfig` warns (`dotnet_style_require_accessibility_modifiers = never`). The one keep: `{ get; private set; }` where the property's getter is wider — there `private` changes accessibility and is required.
- **Always use braces**, even for single-line `if`/`for`/`while` bodies (`csharp_prefer_braces`).
- **Don't pre-wrap lines.** ~170 columns is fine; gratuitous carriage returns hurt readability. `using` directives and method chains do not need to be broken early.
- **One top-level type per file.** A file holding several classes is a smell — split it; the only exception is tightly-coupled nested/private types of a single public type.
- **Frozen `[Alias]` on persisted/wire serializable state.** Every `[GenerateSerializer]` type that is persisted as grain state or crosses the wire carries an `[Alias]`; never suppress `ORLEANS0010`. Commands: `[Alias(nameof(TheCommand))]` (ADR 0010, wire id is intentionally the concrete simple name). **Persisted grain state**: a **frozen string literal** `[Alias("StateName")]`, not `nameof` — it must still deserialize after the class is renamed (ADR 0017).
