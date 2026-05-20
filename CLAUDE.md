# Edict — Claude Instructions

Edict is a CQRS, event-driven framework built on Microsoft Orleans. It is a **library**, not an application. A sample Aspire web/silo app and a shipped in-memory Test Framework come later. Read `CONTEXT.md` for the domain language and `docs/adr/` for the load-bearing decisions before changing architecture.

## Stack
- C# / .NET 10
- Microsoft Orleans (grains, implicit stream subscriptions)
- Azure Queue Storage stream provider, backed by **Azurite** locally
- Microsoft.Extensions.DependencyInjection and Microsoft.Extensions.Logging
- OpenTelemetry (single `ActivitySource` named `"Edict"`)
- Roslyn source generators + analyzers for boilerplate removal
- Aspire AppHost orchestrates the sample app (web + silo + Azurite)

## Core model (see ADRs)
- **Event-driven, not event-sourced** — no event store, no replay, no rebuild. Never add replay/rehydration; "Projection Builder" forward-consumes the live stream only. (ADR 0001)
- **Commands** are direct grain calls. **Events** travel on streams. A Command Handler raises Events as its output.
- Grain hierarchy: `EdictCommandHandler` (direct call, no dedup) and `EdictIdempotencyBase` → consumer event handlers / consumer sagas / `EdictProjectionBuilder` / `EdictTableProjectionBuilder` (implicit subscription + idempotency base). No `Grain` suffix anywhere (ADR 0017); consumer subclasses are `{Name}{Role}` (e.g. `OrderCommandHandler`).
- **Idempotency is in the base, not opt-in.** Bounded per-consumer `EventId` ring, committed *after* `HandleAsync` succeeds. (ADR 0002)
- Stream address is `(eventTypeName, sourceAggregateGuid)`; consumers are per-aggregate by default; a fixed-Guid singleton is the explicit global escape hatch.

## Assembly structure (ADR 0014, 0015)
- **`Edict.Contracts`** — shared kernel: `EdictCommand`, `EdictEvent`, attributes, `IEdictSender`, `IEdictTableRepository`, and the internal write-store seam. No Orleans server runtime.
- **`Edict.Core`** — persistence-agnostic grain runtime: `EdictCommandHandler`, `EdictIdempotencyBase`, `EdictProjectionBuilder`, `EdictTableProjectionBuilder`. Foldered by concept: `Commands/`, `Idempotency/`, `Projections/`, `Sagas/`. Must not depend on `Azure.*`.
- **`Edict.Telemetry`** — `ActivitySource`, span extensions, and the ADR-0003 stream-hop trace capture (`RequestContext`). References `Orleans.Core`, not the server runtime.
- **`Edict.Azure`** — Azure-specific implementations: queue stream provider, `IEdictTableRepository` implementation, write-store implementation. The only assembly that may depend on `Azure.*`.
- **`Edict.Generators`** / **`Edict.Analyzers`** — Roslyn source generators and analyzers (`netstandard2.0`). Shared FQN constants live in a single `EdictWellKnownNames.cs` `<Compile>`-linked into both (no shared runtime assembly).
- **Placement rule**: consumer-typed → `Edict.Contracts`; grain logic → `Edict.Core`; Azure-specific → `Edict.Azure`; telemetry helpers → `Edict.Telemetry`; generator/analyzer → the respective project.

## Conventions
- Never use namespace-qualified types inline — always add a `using` directive; use a `using` alias only if names collide.
- Every Edict grain is declared `partial` (source generators emit the other half). An analyzer must error if it is not.
- **Brand-prefix rule**: a type is `Edict`-prefixed iff **(a)** a consumer types it (derives from it, applies it as an attribute, or receives/returns it) **or (b)** it is an inheritance root shared by the consumer-facing grain bases. Consumer-facing surface: `EdictCommand`, `EdictEvent`, `EdictCommandResult`, `EdictRejectionReason`, `[EdictRouteKey]`, `[EdictStream]`, `[EdictTelemeterized]`, `EdictCommandHandler`, `EdictProjectionBuilder`, `EdictTableProjectionBuilder`, `EdictIdempotencyBase` (clause b), `IEdictSender`, `IEdictTableRepository`. No `Grain` suffix; consumer subclasses are `{Name}{Role}`. Internals (sender implementation, command-route resolver, dedup state) stay bare; raw Orleans test doubles that truly derive from `Grain` keep "Grain". (ADR 0017, supersedes 0013)
- `[EdictTelemeterized]` goes on **primitive properties only**; a generator emits the OTEL tag-writing code. Placing it on a non-primitive is a **compile error** — do not weaken this to a warning.
- Dedup delivery is a template method: the base owns the stream-observer callback and calls the subclass's `HandleAsync`. Subclasses never hand-roll dedup or the dedup guard.
- The consuming grain declares its own stream/subscription — the `EdictIdempotencyBase` base never decides which stream.
- Trace context (`TraceId`/`SpanId`/`TraceState`) lives on the base `EdictEvent` class and stitches `Command → Publish → Handle` as **parent-child** spans across the stream hop. (ADR 0003)
- Logging is `ILogger<T>`, structured, no custom logging abstraction. Do **not** log-narrate the command/event flow — spans are the observability mechanism. A thrown handler logs `Error` with the `EventId`. No `Console.WriteLine`.
- No commercially licensed dependencies (FluentAssertions is banned for this reason).
- No redundant `private` — members are private by default, so omit the keyword (`.editorconfig` warns via `dotnet_style_require_accessibility_modifiers = never`). Keep `private` only where it changes accessibility, e.g. `{ get; private set; }` on a wider property.
- Always use braces, even single-line `if`/`for`/`while` bodies (`csharp_prefer_braces`).
- Don't pre-wrap lines; ~170 columns is fine. Gratuitous carriage returns hurt readability.
- One top-level type per file. A file with many classes is a smell — split it.
- When a project grows past a handful of files, fold by concept (or feature) into subfolders — see `Edict.Core` for the canonical example. Namespace follows folder.
- Every `[GenerateSerializer]` type that is persisted or crosses the wire carries an `[Alias]`; never suppress `ORLEANS0010`. Commands use `[Alias(nameof(TheCommand))]` (ADR 0010). **Persisted grain state** uses a **frozen string literal** `[Alias]` (must survive a class rename — ADR 0017), not `nameof`.

## Testing (ADR 0016)
- **`Edict.Core.Tests`** — mechanism *logic* with no real backend (dedup-ring semantics, projection orchestration, command routing). In-memory streams/stores, no Testcontainers — the fast inner loop. Reaching for Azurite in Core.Tests is a smell.
- **`Edict.Azure.Tests`** — full mechanism battery against real **Azurite via Testcontainers**: at-least-once redelivery + dedup realism (the ADR-0002 proof lives here) and table-projection persistence. This is the provider conformance suite.
- **`Edict.Telemetry.Tests`** — `ActivityListener` assertions on the span tree and `edict.*` tags.
- **`Edict.Generators.Tests`** / **`Edict.Analyzers.Tests`** — generator output shape and `EDICT00x` diagnostic coverage in their own projects.
- **`Edict.Architecture.Tests`** — `BoundaryTests` and `TypePlacementTests` boundary guards.
- The **shipped Test Framework** (`Edict.Testing`) is **in-memory** (memory streams), boots the consumer's grains with Edict auto-wired, and exposes a single Verify-shaped timeline (Commands, Events, Projection/Saga state). It does **not** capture traces.
- Strongly favour integration tests and **Verify** snapshot tests over long `Assert` chains. Never commit `.received.*` files.
- Test projects mirror the folder layout of the project they test: one test file per source class, foldered the same way. A single grab-bag test file covering many classes is a smell.
- Test names: `Subject_Should{Outcome}[_When{Condition}]`. `Subject` is the method under test when one exists, else a scenario noun (e.g. `EDICT001`, `CommandPipeline`). `_When…` only when there is a condition. If `Class.Method` would push a Verify snapshot filename past ~90 chars, the test scope is too broad — split it, don't truncate or hash.
- Verify snapshots live in a flat `{TestProject}/Snapshots/` directory (a `ModuleInitializer` sets `Verifier.DerivePathInfo`). Contributors must `git config core.longpaths true` on Windows.
- The Sample app uses **no in-memory infrastructure** — Azure Table grain storage (PubSub + dedup ring), Azure Queue streams, Azure Table projections; `Sample.AppHost` provisions Azurite. In-memory wiring belongs to the shipped Test Framework, never the sample.

## Skills available
Skills are auto-loaded on demand. Key skills for this project:
- **csharp** — C# naming, using directives, framework project structure (triggers on `.cs` files)
- **testing** — xUnit/Verify/Testcontainers conventions, what not to do (triggers on test files)
- **tdd** — red-green-refactor loop
- **diagnose** — disciplined debugging loop
- **grill-me** / **grill-with-docs** — alignment sessions before building
- **to-issues** / **to-prd** — planning and issue creation
