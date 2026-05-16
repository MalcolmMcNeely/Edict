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
- Grain hierarchy: `CommandHandlerGrain` (direct call, no dedup) and `EventDeduplicationGrain` → `EventHandlerGrain` / `SagaGrain` / `ProjectionBuilderGrain` (implicit subscription + dedup base).
- **Idempotency is in the base, not opt-in.** Bounded per-consumer `EventId` ring, committed *after* `HandleAsync` succeeds. (ADR 0002)
- Stream address is `(eventTypeName, sourceAggregateGuid)`; consumers are per-aggregate by default; a fixed-Guid singleton is the explicit global escape hatch.

## Conventions
- Never use namespace-qualified types inline — always add a `using` directive; use a `using` alias only if names collide.
- Every Edict grain is declared `partial` (source generators emit the other half). An analyzer must error if it is not.
- Base classes are named `Event` and `Command` (no prefix). Concrete events/commands derive from them.
- `[Telemeterized]` goes on **primitive properties only**; a generator emits the OTEL tag-writing code. Placing it on a non-primitive is a **compile error** — do not weaken this to a warning.
- Dedup delivery is a template method: the base owns the stream-observer callback and calls the subclass's `HandleAsync`. Subclasses never hand-roll dedup or the dedup guard.
- The consuming grain declares its own stream/subscription — the `EventDeduplicationGrain` base never decides which stream.
- Trace context (`TraceId`/`SpanId`/`TraceState`) lives on the base `Event` class and stitches `Command → Publish → Handle` as **parent-child** spans across the stream hop. (ADR 0003)
- Logging is `ILogger<T>`, structured, no custom logging abstraction. Do **not** log-narrate the command/event flow — spans are the observability mechanism. A thrown handler logs `Error` with the `EventId`. No `Console.WriteLine`.
- No commercially licensed dependencies (FluentAssertions is banned for this reason).

## Testing
- **Edict's own tests** use the Orleans `TestCluster` against **Azurite via Testcontainers** (real at-least-once redelivery) plus `ActivityListener` assertions on the span tree and `edict.*` tags.
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
