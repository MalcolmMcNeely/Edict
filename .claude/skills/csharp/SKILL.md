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

## Brand-prefix rule (ADR 0013)

A type is `Edict`-prefixed **if and only if a consumer types it** — derives from it, applies it as an attribute, or receives/returns it. The prefix keeps signalling "this is your contract with the framework."

**Consumer-facing (always `Edict`-prefixed):**
`EdictCommand`, `EdictEvent`, `EdictCommandResult`, `EdictRejectionReason`, `[EdictRouteKey]`, `[EdictStream]`, `[EdictTelemeterized]`, `EdictCommandHandlerGrain`, `EdictEventDeduplicationGrain`, `EdictProjectionBuilderGrain`, `EdictTableProjectionBuilderGrain`, `IEdictSender`, `IEdictTableRepository`.

**Internal (stays bare):** sender implementation, command-route resolver, dedup state, exception types. If a consumer never names it, it does not carry the prefix.

## Assembly boundaries (ADR 0014, 0015)

| Assembly | What goes here |
|---|---|
| `Edict.Contracts` | Consumer-typed surface: `EdictCommand`, `EdictEvent`, attributes, `IEdictSender`, `IEdictTableRepository`, and the internal write-store seam. No Orleans server runtime. |
| `Edict.Core` | Persistence-agnostic grain runtime: `EdictCommandHandlerGrain`, `EdictEventDeduplicationGrain`, `EdictProjectionBuilderGrain`, `EdictTableProjectionBuilderGrain`. Must not depend on `Azure.*`. |
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
  Commands/     – EdictCommandHandlerGrain, sender, routing
  Dedup/        – EdictEventDeduplicationGrain and dedup state (shared inheritance root)
  Projections/  – EdictProjectionBuilderGrain and EdictTableProjectionBuilderGrain
  Sagas/        – (future) saga base
  Serialization/
  TableStorage/ – framework-internal write-store seam
```

`Dedup/` is the shared inheritance root — it is not an Events concept and does not live under `Events/`. Add new concept sub-folders only when a clear grouping emerges.
