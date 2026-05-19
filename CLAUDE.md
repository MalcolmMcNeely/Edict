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
- Grain hierarchy: `EdictCommandHandlerGrain` (direct call, no dedup) and `EdictEventDeduplicationGrain` → consumer event-handler grains / consumer saga grains / `EdictProjectionBuilderGrain` / `EdictTableProjectionBuilderGrain` (implicit subscription + dedup base).
- **Idempotency is in the base, not opt-in.** Bounded per-consumer `EventId` ring, committed *after* `HandleAsync` succeeds. (ADR 0002)
- Stream address is `(eventTypeName, sourceAggregateGuid)`; consumers are per-aggregate by default; a fixed-Guid singleton is the explicit global escape hatch.

## Assembly structure (ADR 0014, 0015)
- **`Edict.Contracts`** — shared kernel: `EdictCommand`, `EdictEvent`, attributes, `IEdictSender`, `IEdictTableRepository`, and the internal write-store seam. No Orleans server runtime.
- **`Edict.Core`** — persistence-agnostic grain runtime: `EdictCommandHandlerGrain`, `EdictEventDeduplicationGrain`, `EdictProjectionBuilderGrain`, `EdictTableProjectionBuilderGrain`. Foldered by concept: `Commands/`, `Dedup/`, `Projections/`, `Sagas/`. Must not depend on `Azure.*`.
- **`Edict.Telemetry`** — `ActivitySource`, span extensions, and the ADR-0003 stream-hop trace capture (`RequestContext`). References `Orleans.Core`, not the server runtime.
- **`Edict.Azure`** — Azure-specific implementations: queue stream provider, `IEdictTableRepository` implementation, write-store implementation. The only assembly that may depend on `Azure.*`.
- **`Edict.Generators`** / **`Edict.Analyzers`** — Roslyn source generators and analyzers (`netstandard2.0`). Shared FQN constants live in a single `EdictWellKnownNames.cs` `<Compile>`-linked into both (no shared runtime assembly).
- **Placement rule**: consumer-typed → `Edict.Contracts`; grain logic → `Edict.Core`; Azure-specific → `Edict.Azure`; telemetry helpers → `Edict.Telemetry`; generator/analyzer → the respective project.

## Conventions
- Never use namespace-qualified types inline — always add a `using` directive; use a `using` alias only if names collide.
- Every Edict grain is declared `partial` (source generators emit the other half). An analyzer must error if it is not.
- **Brand-prefix rule**: a type is `Edict`-prefixed if and only if a consumer types it — derives from it, applies it as an attribute, or receives/returns it. Consumer-facing surface: `EdictCommand`, `EdictEvent`, `EdictCommandResult`, `EdictRejectionReason`, `[EdictRouteKey]`, `[EdictStream]`, `[EdictTelemeterized]`, `EdictCommandHandlerGrain`, `EdictEventDeduplicationGrain`, `EdictProjectionBuilderGrain`, `EdictTableProjectionBuilderGrain`, `IEdictSender`, `IEdictTableRepository`. Internals (sender implementation, command-route resolver, dedup state) stay bare. (ADR 0013)
- `[EdictTelemeterized]` goes on **primitive properties only**; a generator emits the OTEL tag-writing code. Placing it on a non-primitive is a **compile error** — do not weaken this to a warning.
- Dedup delivery is a template method: the base owns the stream-observer callback and calls the subclass's `HandleAsync`. Subclasses never hand-roll dedup or the dedup guard.
- The consuming grain declares its own stream/subscription — the `EdictEventDeduplicationGrain` base never decides which stream.
- Trace context (`TraceId`/`SpanId`/`TraceState`) lives on the base `EdictEvent` class and stitches `Command → Publish → Handle` as **parent-child** spans across the stream hop. (ADR 0003)
- Logging is `ILogger<T>`, structured, no custom logging abstraction. Do **not** log-narrate the command/event flow — spans are the observability mechanism. A thrown handler logs `Error` with the `EventId`. No `Console.WriteLine`.
- No commercially licensed dependencies (FluentAssertions is banned for this reason).

## Testing (ADR 0016)
- **`Edict.Core.Tests`** — mechanism *logic* with no real backend (dedup-ring semantics, projection orchestration, command routing). In-memory streams/stores, no Testcontainers — the fast inner loop. Reaching for Azurite in Core.Tests is a smell.
- **`Edict.Azure.Tests`** — full mechanism battery against real **Azurite via Testcontainers**: at-least-once redelivery + dedup realism (the ADR-0002 proof lives here) and table-projection persistence. This is the provider conformance suite.
- **`Edict.Telemetry.Tests`** — `ActivityListener` assertions on the span tree and `edict.*` tags.
- **`Edict.Generators.Tests`** / **`Edict.Analyzers.Tests`** — generator output shape and `EDICT00x` diagnostic coverage in their own projects.
- **`Edict.Architecture.Tests`** — `BoundaryTests` and `TypePlacementTests` boundary guards.
- The **shipped Test Framework** (`Edict.Testing`) is **in-memory** (memory streams), boots the consumer's grains with Edict auto-wired, and exposes a single Verify-shaped timeline (Commands, Events, Projection/Saga state). It does **not** capture traces.
- Strongly favour integration tests and **Verify** snapshot tests over long `Assert` chains. Never commit `.received.*` files.

## Skills available
Skills are auto-loaded on demand. Key skills for this project:
- **csharp** — C# naming, using directives, framework project structure (triggers on `.cs` files)
- **testing** — xUnit/Verify/Testcontainers conventions, what not to do (triggers on test files)
- **tdd** — red-green-refactor loop
- **diagnose** — disciplined debugging loop
- **grill-me** / **grill-with-docs** — alignment sessions before building
- **to-issues** / **to-prd** — planning and issue creation
