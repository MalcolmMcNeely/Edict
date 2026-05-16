---
name: testing
description: Use this skill when creating, modifying, or reviewing tests in this repo — including xUnit integration tests, bUnit Blazor component tests, and Testcontainers fixtures. Covers test philosophy, project structure, approved libraries, and what not to do.
---

# Test Philosophy

## Guiding principle

Test external behaviour, not implementation details. A test should remain valid even if the internal structure of the code under test changes entirely. If a test breaks because of a rename or a refactor that doesn't change observable behaviour, the test was written at the wrong level.

## Integration-first

Prefer integration tests over unit tests. Infrastructure — EF Core, blob storage, background jobs — must be tested through the full stack, not mocked away. Mocking infrastructure hides the bugs that matter most.

- Use `WebApplicationFactory` to host the application in-process.
- Use **Testcontainers** to spin up real Postgres and Azurite (Azure Blob Storage) containers per test run.
- Never mock `DbContext`, `IBlobRepository`, or any infrastructure interface that has a real Testcontainers equivalent.

## What to test in isolation

Pure domain logic — value objects, state machines, validation rules — may be tested in isolation when the test is meaningfully simpler and the logic has no infrastructure dependency.

## Test project naming and structure

Each feature module gets one test project: `Covenant.{Module}.Tests`. Integration tests, domain unit tests, and **bUnit component tests** for that feature's Blazor pages all live together in that project.

Cross-cutting UI that belongs to no single feature domain (app shell, layout, navigation, theme, and app-level aggregation pages such as the home dashboard) lives in `Covenant.Web.Tests`. That project references `Covenant.Web` and any feature projects whose interfaces are directly mocked in tests. Do **not** add a `Covenant.Web` reference to a feature test project to host misplaced shell tests.

The folder structure inside a test project must mirror the source project it tests:

```
Covenant.Templates/          Covenant.Templates.Tests/
  Entities/                    Services/
  Services/                    Components/
  Models/                      Infrastructure/

Covenant.Web/                Covenant.Web.Tests/
  Components/Layout/           Components/Layout/
  Components/Features/Home/    Components/Features/Home/
  Theme/                       Theme/
```

Shared test infrastructure (fixtures, stubs) goes in an `Infrastructure/` subfolder — create it lazily when the first shared fixture is needed.

## Evaluation harness (AI features)

AI-driven features (merge field extraction, contract generation, redlining) are tested with a separate evaluation harness:

- Fixtures are `(input, template, expected_output)` triples stored as files.
- Scoring: **field accuracy** (exact match for structured fields, LLM-as-judge for free-text), **no hallucinated clauses**, **no dropped clauses**, **format integrity** (DOCX round-trip check).
- The harness runs in CI and outputs a JSON report per run. A regression in score is a failing build.
- LLM-as-judge assertions go in `Covenant.Eval` — never mixed into feature test projects.

## Arrange / Act / Assert

Structure every test with a clear Arrange / Act / Assert separation. Name tests in the form `{Method or scenario}_{context}_{expected outcome}`.

## Approved libraries

| Purpose | Library |
|---|---|
| Test framework | **xUnit** |
| Assertions | **xUnit built-ins** (`Assert.*`) |
| Snapshot testing | **Verify** (`Verify.Xunit`) |
| Blazor component testing | **bUnit** |
| Containers | **Testcontainers** |

**FluentAssertions is banned** — it moved to a commercial license. Do not add it or any wrapper around it.

Use **Verify** when a return value has more than one field to assert. Do not write `Assert.Equal` chains and add Verify later — use it on first write.

Verify scrubs Guids and DateTimes **by default**, replacing them with deterministic placeholders (`Guid_1`, `DateTime_1`, etc.). Do not add `.IgnoreMembersWithType<Guid>()` or `.IgnoreMembersWithType<DateTimeOffset>()` — ignoring removes fields from the snapshot entirely, which means the snapshot never verifies those fields exist. Let the default scrubbing work. Use `DontScrubGuids()` only when you explicitly want raw Guid values in a snapshot.

If a Guid is semantically important (e.g. ownership or a foreign-key link), assert it separately with `Assert.Equal` alongside the plain `Verify(...)` call.

Never commit `.received.*` files — only `.verified.*` files are committed.

Use **bUnit** for Blazor component tests where you need to verify rendering, event handling, or component interaction in isolation. Any component that uses MudBlazor components requires `ctx.Services.AddMudServices()` in the test setup:

```csharp
using var ctx = new TestContext();
ctx.Services.AddMudServices();
// render component under test
```

## What not to do

- Do not test that a method was called (verify outcomes, not interactions).
- Do not use `Moq` or any mocking library for infrastructure boundaries — use real containers.
- Do not share mutable state between tests.
- Do not assert on log output or internal exception messages unless the message is part of the public contract.
- Do not use FluentAssertions — it is commercially licensed.
- Do not add section divider comments (e.g. `// ── Cycle 2 ──`) inside test files. If you feel the need to separate groups of tests with a divider, split them into separate files instead.
