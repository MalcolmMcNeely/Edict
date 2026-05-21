# Edict — Claude Instructions

Edict is a CQRS, event-driven framework built on Microsoft Orleans. It is a **library**, not an application.

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

- Never use namespace-qualified types inline — always add a `using` directive; use a `using` alias only if names collide.
- No redundant `private` — members are private by default, so omit the keyword (`.editorconfig` warns via `dotnet_style_require_accessibility_modifiers = never`). Keep `private` only where it changes accessibility, e.g. `{ get; private set; }` on a wider property.
- Always use braces, even single-line `if`/`for`/`while` bodies (`csharp_prefer_braces`).
- Don't pre-wrap lines; ~170 columns is fine. Gratuitous carriage returns hurt readability.
- One top-level type per file. A file with many classes is a smell — split it.
- When a project grows past a handful of files, fold by concept (or feature) into subfolders. Namespace follows folder.
- Logging is `ILogger<T>`, structured, no custom logging abstraction. Do **not** log-narrate the command/event flow — spans are the observability mechanism. A thrown handler logs `Error` with the `EventId`. No `Console.WriteLine`.
- No commercially licensed dependencies (FluentAssertions is banned for this reason).

## Comment policy

- **XML doc (`///`)** is required on the consumer-facing `Edict*` surface in `Edict.Contracts` and on the public bases in `Edict.Core`. It is forbidden on internal-only types unless the type's purpose is non-obvious from its name — in that case, prefer renaming the type over adding a summary.
- **Inline (`//`)** comments are only for non-obvious WHY, and the prose must stand alone. Do not cite ADR numbers — if the comment only earns its keep via a doc pointer, rewrite the prose so it stands alone or delete the comment. Comments that restate what the code does should be deleted.
- **Test scaffolding** — `// Arrange`, `// Act`, `// Assert` markers are a permitted readability convention in tests.

## Skills available

Skills are auto-loaded on demand. Key skills for this project:

- **csharp** — C# naming, using directives, framework project structure (triggers on `.cs` files)
- **blazor** — Blazor component rules (triggers on `.razor` files)
- **testing** — xUnit/Verify/Testcontainers conventions, what not to do (triggers on test files)
- **tdd** — red-green-refactor loop
- **diagnose** — disciplined debugging loop
- **grill-me** / **grill-with-docs** — alignment sessions before building
- **to-issues** / **to-prd** — planning and issue creation
