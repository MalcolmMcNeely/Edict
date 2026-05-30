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

## Message authoring shape (ADR 0046)

Concrete `EdictCommand` and `EdictEvent` subclasses are `partial record` types authored as a primary constructor only. Attach attributes via the `[property: ...]` target on the primary-ctor parameter; do **not** redeclare primary-ctor parameters as body properties. The IDE greys body redeclarations because the compiler has already synthesised them — a consumer reading the type cannot tell whether the redeclaration is mandatory framework ceremony or stylistic. The wire shape is identical and the generator walks `IPropertySymbol`, so authoring form is a clarity decision, not a correctness one.

```csharp
// Bad (body redeclares primary-ctor parameters)
public sealed partial record PlaceOrderCommand(Guid OrderId, string CustomerReference) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string CustomerReference { get; init; } = CustomerReference;
}

// Good (primary ctor only, attributes via [property: ...])
public sealed partial record PlaceOrderCommand(
    [property: EdictRouteKey] Guid OrderId,
    string CustomerReference) : EdictCommand;
```

## Persisted state authoring shape (ADR 0046)

`IEdictPersistedState` implementations — aggregate state, saga progress, table-projection row POCOs — are `sealed class` with `{ get; set; }` properties. The framework's authoring contract for state is in-place mutation inside `Handle` (`State.Status = ...`, `Progress.Stage = ...`); a record with `init` setters would force `State = State with { ... }` per mutation, which fights the documented idiom and is not even possible on the grain base's read-only `State` property. The class-vs-record rule reaches `IEdictPersistedState` implementations only — other DTOs (`MetricsSnapshot`, ad-hoc DI carriers) are out of scope.

```csharp
[GenerateSerializer]
[Alias("Sample.Silo.Orders.OrderState")]
public sealed class OrderState : IEdictPersistedState
{
    [Id(0)]
    public OrderStatus Status { get; set; } = OrderStatus.Open;
}
```

## Comment policy

Comments are differentiated by kind. Each kind has a different bar.

- **XML doc (`///`)** is **required** on the consumer-facing `Edict*` surface in `Edict.Contracts` and on the public bases in `Edict.Core`. It is **forbidden** on internal-only types unless the type's purpose is non-obvious from its name — in that case, prefer renaming the type over adding a summary.
- **Inline (`//`)** is only for non-obvious **WHY**, and the prose must stand alone. Do **not** cite ADR numbers — if the comment only earns its keep via a doc pointer, rewrite the prose so it stands alone or delete the comment. Comments that restate **what** the code does should be deleted; well-named identifiers already do that.
- **Test scaffolding** — `// Arrange`, `// Act`, `// Assert` markers are a permitted readability convention in test bodies.

## Other conventions

- **No redundant `private`.** Members are private by default — omit the keyword. `.editorconfig` warns (`dotnet_style_require_accessibility_modifiers = never`). The one keep: `{ get; private set; }` where the property's getter is wider — there `private` changes accessibility and is required.
- **Always use braces**, even for single-line `if`/`for`/`while` bodies (`csharp_prefer_braces`).
- **Don't pre-wrap lines.** ~170 columns is fine; gratuitous carriage returns hurt readability. `using` directives and method chains do not need to be broken early.
- **One top-level type per file.** A file holding several classes is a smell — split it; the only exception is tightly-coupled nested/private types of a single public type.
- **Frozen `[Alias]` on persisted/wire serializable state.** Every `[GenerateSerializer]` type that is persisted as grain state or crosses the wire carries an `[Alias]`; never suppress `ORLEANS0010`. Commands: `[Alias(nameof(TheCommand))]` (ADR 0010, wire id is intentionally the concrete simple name). **Persisted grain state**: a **frozen string literal** `[Alias("StateName")]`, not `nameof` — it must still deserialize after the class is renamed (ADR 0017).
