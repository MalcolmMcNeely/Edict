# Edict — Claude Instructions

Edict is a CQRS, event-driven framework built on Microsoft Orleans. It is a **library**, not an application.

## Project map

The repo has 47 `.csproj` files; they collapse into six logical groups. Per-project tests follow a uniform pattern (every shipping project has a paired `.Tests`, e.g. `Edict.Core.Tests`, `Edict.Kafka.Tests`) and are not listed individually — only the projects that break the pattern get their own row. Conformance runs as **two axis batteries** (streaming + persistence, ADR-0054, supersedes 0027): Azure's paired tests split into `Edict.Azure.Streaming.Tests` + `Edict.Azure.Persistence.Tests` (mirroring ADR-0042), while single-axis `Edict.Kafka.Tests` and `Edict.Postgres.Tests` each bind their one axis provider's conformance alongside their bespoke seam tests, and `Edict.Pairing.Tests` carries the cross-pairing smoke.

### Framework (consumer-facing, production)

| Project | Purpose |
|---|---|
| `Edict.Contracts` | Consumer-typed wire surface — `EdictCommand`, `EdictEvent`, attributes, `IEdictSender` |
| `Edict.Core` | Persistence-agnostic grain runtime — `EdictCommandHandler`, `EdictProjectionBuilder`, etc. |
| `Edict.Telemetry` | `ActivitySource`, span extensions, stream-hop trace capture |
| `Edict.Analyzers` | Roslyn analyzers (`EDICT00x` diagnostics) |
| `Edict.Generators` | Roslyn source generators |

### Streaming providers (consumer-facing, production)

| Project | Purpose |
|---|---|
| `Edict.Azure.Streaming` | Azure Queue streaming + claim-check |
| `Edict.Kafka` | Custom Kafka stream provider (ADR-0028) |

### Persistence providers (consumer-facing, production)

| Project | Purpose |
|---|---|
| `Edict.Azure.Persistence` | Azure Table grain-state + blob projection |
| `Edict.Postgres` | Postgres persistence + custom grain storage (ADR-0029) |

### Agentic tooling (consumer-facing, production)

| Project | Purpose |
|---|---|
| `Edict.ClaudeSkills` | `dotnet tool` that installs the five consumer skills into `.claude/skills/` |
| `Edict.Mcp` | MCP server exposing the six `edict_*` tools |

### Consumer demonstration (sample app + the test surface it consumes)

| Project | Purpose |
|---|---|
| `Edict.Testing` | Consumer-facing test fixtures (`InProcPublishEventExecutor`, probes) |
| `Sample.{Contracts,Domain}` | Shared sample contracts and domain |
| `Sample.Azure.*` | Azure-backed sample (AppHost, Silo, Web) |
| `Sample.KafkaPostgres.*` | Kafka+Postgres-backed sample (AppHost, Silo, Web) |
| `Sample.Azure.Silo.Tests` | Exercises `Edict.Testing` — the consumer-test reference (no KafkaPostgres parallel yet) |
| `Sample.{Web.Components,ServiceDefaults}` | Razor components, Aspire defaults |

### Harness infrastructure (bench and conformance, not production)

See `CONTEXT.md` for the "substrate" disambiguation — this group is the harness-library sense, not the production-backend sense.

| Project | Purpose |
|---|---|
| `Edict.Substrate` | `ISubstrate` seam — harness-to-backend abstraction (ADR-0030) |
| `Edict.Substrate.Azurite` | Local Azurite-backed substrate |
| `Edict.Substrate.KafkaPostgres` | Kafka+Postgres-backed substrate |
| `Edict.Tests.Conformance` | Streaming + persistence axis-conformance batteries and their dumb references (ADR-0054) |
| `Edict.Benchmarks` | Microbenchmarks |
| `Edict.Benchmarks.Throughput` | Throughput harness |
| `Edict.Benchmarks.Throughput.Tests` | xUnit wrapper — skip by default (slow, flaky) |

### Cross-cutting tests

| Project | Purpose |
|---|---|
| `Edict.Architecture.Tests` | Public-surface allow-list, type placement, boundary checks, conformance binding-completeness guard |
| `Edict.AgenticTooling.Architecture.Tests` | Skill ↔ MCP-tool interlock drift guard |
| `Edict.Pairing.Tests` | Bucket-4 cross-pairing smoke — composition boot + write-fault ∧ redelivery, per shipped pairing (ADR-0054) |

## Reference index

When the conversation touches these topics, read the linked depth before answering.

| Topic | Where to read |
|---|---|
| Contracts, wire shape, attributes, `[Alias]` | `edict-contracts` skill body + ADR-0006, 0007, 0046 |
| Idempotency, dedup, redelivery | `docs/usage/concepts/idempotency.md` + ADR-0002 |
| Outbox effects, draining | `docs/usage/concepts/events.md` + ADR-0015 |
| Dead-letter, forensics | `docs/usage/concepts/dead-letter.md` + ADR-0018, 0041 |
| Claim-check, oversized payloads | `docs/usage/concepts/claim-check.md` + ADR-0020 |
| Saga, multi-step workflows | `docs/usage/concepts/sagas.md` + ADR-0016 |
| Projections, read models (State + List species) | `docs/usage/concepts/projections.md` + ADR-0061, 0011, 0013 |
| Source generation, codegen ordering | `csharp` skill body + ADR-0005, 0033 |
| Substrate, harness, conformance | ADR-0054 (axis batteries, supersedes 0027), 0030 |
| Configuration, options, tuning | `docs/configuration` + ADR-0023 |
| Agentic tooling, skill ↔ MCP interlock | ADR-0044, the interlock test in `Edict.AgenticTooling.Architecture.Tests` |

## Before you touch the repo

- Read `CONTEXT.md` before any domain work — it is the glossary, one sentence per term.
- Read `docs/adr/` before any architectural change — the decisions and their rationale live there.
- Follow the relevant skill when editing `.cs` files (`csharp`), `.razor` files (`blazor`), or tests (`testing`).

## Stack

- C# / .NET 10
- Microsoft Orleans (grains, implicit stream subscriptions)
- Azure Queue Storage stream provider, backed by **Azurite** locally
- Microsoft.Extensions.DependencyInjection and Microsoft.Extensions.Logging
- OpenTelemetry (single `ActivitySource` named `"Edict"`)
- Roslyn source generators + analyzers for boilerplate removal
- Aspire AppHost orchestrates the sample app (web + silo + Azurite)

## Conventions

- **Never abbreviate identifiers.** Variable, parameter, field, and property names use the full word. `CancellationToken cancellationToken`, not `ct`. `IServiceProvider serviceProvider`, not `sp`. `Exception exception`, not `ex`. `ILogger<T> logger`, not `log`. The only allowed shortenings are domain acronyms that are proper nouns and the `string[] args` entry-point parameter.
- Never use namespace-qualified types inline — always add a `using` directive; use a `using` alias only if names collide.
- No redundant `private` — members are private by default, so omit the keyword (`.editorconfig` warns via `dotnet_style_require_accessibility_modifiers = never`). Keep `private` only where it changes accessibility, e.g. `{ get; private set; }` on a wider property.
- Always use braces, even single-line `if`/`for`/`while` bodies (`csharp_prefer_braces`).
- Don't pre-wrap lines; ~170 columns is fine. Gratuitous carriage returns hurt readability.
- One top-level type per file. A file with many classes is a smell — split it.
- When a project grows past a handful of files, fold by concept (or feature) into subfolders. Namespace follows folder.
- Place a file (test or type) where its subject conceptually belongs, never in whichever project happens to already reference what it needs. Reference-convenience placement — e.g. a cross-provider test dropped into one provider's test project "because it references everything" — confuses readers and usually signals the file has no real conceptual owner (it may be redundant, or belong in a cross-cutting project like `Edict.Architecture.Tests`).
- Logging is `ILogger<T>`, structured, no custom logging abstraction. Do **not** log-narrate the command/event flow — spans are the observability mechanism. A thrown handler logs `Error` with the `EventId`. No `Console.WriteLine`.
- No commercially licensed dependencies (FluentAssertions is banned for this reason).

## Comment policy

- **XML doc (`///`)** is required on the consumer-facing `Edict*` surface in `Edict.Contracts` and on the public bases in `Edict.Core`. It is forbidden on internal-only types unless the type's purpose is non-obvious from its name — in that case, prefer renaming the type over adding a summary.
- **Inline (`//`)** comments are only for non-obvious WHY, and the prose must stand alone. Do not cite ADR numbers — if the comment only earns its keep via a doc pointer, rewrite the prose so it stands alone or delete the comment. Comments that restate what the code does should be deleted.
- **Test scaffolding** — `// Arrange`, `// Act`, `// Assert` markers are a permitted readability convention in tests.

## Exception philosophy

Exceptions are reserved for things that prevent the framework from running correctly — wiring faults at boot, unrepresentable program states at runtime, and the safety net itself. The consumer-facing runtime path is built to **not throw at the consumer**: framework throws are caught and dead-lettered, expected rejection flows through typed results. Every production throw is an `Edict*`-typed exception so dead-letter rows, span events, and any catch site all key on a stable type. ADR-0041 is the contract.

- **Boot: wiring throws are loud and aggregated.** Missing config, missing provider, missing client → `EdictWiringException` from the extension call site, or `EdictWiringValidator` at host start. A misconfigured silo fails once on startup with one aggregated message, not on every `HandleAsync` call.
- **Runtime: framework throws never reach the consumer.** Per-cause-narrative `Edict*` types (`EdictUnregisteredTypeException`, `EdictClaimCheckFetchException`, `EdictSagaCoordinationException`, etc.) are thrown by framework internals, caught by `OutboxHost.ExecuteGroupCapturingAsync`, classified by `DeadLetterFailureClassifier`, and dead-lettered per ADR-0018. A consumer's `HandleAsync` never sees them.
- **Validation rejects through `EdictCommandResult`, not exceptions.** A Command Validator returns `Rejected`; a Command Handler returns `Rejected`. The typed result envelope is the *expected* rejection path. Throws from consumer code propagate to dead-letter (treated as runtime faults), but throwing is not the rejection mechanism.
- **The safety net itself cannot throw.** Nothing reached from `DeadLetterPromoter.Promote()` may throw — it is called from `OutboxHost` outside the engine's per-group catch, so a throw becomes a poison-pill loop (state write skipped, entry stays Pending, next reminder fires the same throw). For unrepresentable causes (unknown `OutboxEffectKind`, `SendCommand` missing `[EdictRouteKey]`, or a forensic body that cannot be materialised or JSON-serialised — a renamed/removed row type, an unserialisable consumer payload), log a warning, increment `PromotionFailureCount` with a bounded `promotion_failure_reason` tag, and return a synthetic dead-letter row whose `ExceptionType` carries a string-marker `Edict*Exception` name. Marker types (`EdictUnsupportedEffectKindException`, `EdictMissingRouteKeyException`, `EdictPromotionSerializationException`) are never instantiated. The body-materialisation steps run inside `Promote`'s outer try/catch, so even an unanticipated throw degrades to this synthetic row rather than escaping.

## Quick reference

The solution is `Edict/Edict.slnx`. Throughput tests are slow and flake-prone — skip unless the task is throughput-specific.

| Action | Command |
|---|---|
| Run the whole suite | `dotnet test Edict/Edict.slnx` |
| Skip throughput tests | exclude `Edict.Benchmarks.Throughput.Tests` from the test set |
| Test a single project | `dotnet test Edict/Edict.<Project>.Tests/Edict.<Project>.Tests.csproj` |
| Run the Azure sample app | `dotnet run --project Sample/Sample.Azure.AppHost` |
| Run the Kafka+Postgres sample app | `dotnet run --project Sample/Sample.KafkaPostgres.AppHost` |
| Verify skill ↔ MCP interlock | `dotnet test Edict/Edict.AgenticTooling.Architecture.Tests/Edict.AgenticTooling.Architecture.Tests.csproj` |

One-time Windows setup: `git config core.longpaths true` (some test fixture paths exceed 260 chars).

## Skills available

A SessionStart hook (`.claude/hooks/inject-skills-on-session-start.ps1`) injects the bodies of `csharp`, `blazor`, `testing`, and `surface-config` at the start of every session, so the full conventions are already in context. A PostToolUse hook (`.claude/hooks/block-style-violations.ps1`) blocks edits that ship known offenders (e.g. `CancellationToken ct`).

Other skills available on demand via the Skill tool:

- **tdd** — red-green-refactor loop
- **diagnose** — disciplined debugging loop
- **grill-me** / **grill-with-docs** — alignment sessions before building
- **to-issues** / **to-prd** — planning and issue creation
