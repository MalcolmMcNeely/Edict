---
name: testing
description: Use this skill when creating, modifying, or reviewing tests in the Edict repo — xUnit, Verify snapshots, Testcontainers/Azurite, generator & analyzer tests. Covers the ADR 0016 project layering, naming, Verify path rules, and what not to do.
---

# Test Philosophy (Edict)

## Guiding principle

Test external behaviour, not implementation details. A test should survive a rename or refactor that does not change observable behaviour. If it breaks on a pure rename, it was written at the wrong level.

## Project layering (ADR 0016)

Each suite has a single, non-overlapping job. Putting a test in the wrong project is the most common mistake here.

| Project | What it tests | Backend |
|---|---|---|
| `Edict.Core.Tests` | Mechanism *logic*: dedup-ring semantics, projection orchestration, command routing | In-memory streams/stores. **No Testcontainers** — fast inner loop. Reaching for Azurite here is a smell. |
| `Edict.Azure.Tests` | Full mechanism battery against real infra: at-least-once redelivery + dedup realism (the ADR-0002 proof), table-projection persistence | **Azurite via Testcontainers** — the provider conformance suite |
| `Edict.Telemetry.Tests` | Span tree + `edict.*` tags | `ActivityListener` |
| `Edict.Generators.Tests` | Generator output shape | Verify snapshots of emitted source |
| `Edict.Analyzers.Tests` | `EDICT00x` diagnostic coverage | analyzer test harness; assert diagnostic **line** positions |
| `Edict.Architecture.Tests` | `BoundaryTests`, `TypePlacementTests` | reflection over assemblies |

The shipped Test Framework (`Edict.Testing`) is the *only* place in-memory wiring is correct for consumer-facing scenarios. The Sample app never uses in-memory infra.

## Test naming

`Subject_Should{Outcome}[_When{Condition}]`.

- `Subject` is the method under test when one exists, else a scenario noun (`EDICT001`, `CommandPipeline`, `ClosedHierarchy`).
- `_When{Condition}` only when there *is* a condition — drop it for unconditional facts.
- Examples: `Send_ShouldReturnRejected_WhenValidatorFails`, `EDICT001_ShouldNotRaise_WhenGrainIsPartial`, `CommandResult_ShouldBeClosedHierarchy`.

Structure every test Arrange / Act / Assert. The `// Arrange`, `// Act`, `// Assert` markers are a permitted readability convention in test bodies — they are the one exception to the general "no comments that restate what the code does" rule.

## Verify

| Purpose | Library |
|---|---|
| Test framework | **xUnit** |
| Assertions | **xUnit built-ins** (`Assert.*`) |
| Snapshot | **Verify** (`Verify.Xunit`) |
| Containers | **Testcontainers** |

- Use **Verify** when a return value has more than one field to assert. Don't write `Assert.Equal` chains and add Verify later — use it on first write.
- Verify scrubs Guids/DateTimes by default (`Guid_1`, `DateTime_1`). Do **not** add `.IgnoreMembersWithType<Guid>()` — ignoring removes the field from the snapshot so its existence is no longer verified. Let default scrubbing work; use `DontScrubGuids()` only when raw values matter.
- If a Guid is semantically load-bearing (ownership, FK link), assert it separately with `Assert.Equal` alongside the `Verify(...)`.
- Snapshots live in a **flat `{TestProject}/Snapshots/` directory** — a `ModuleInitializer` sets `Verifier.DerivePathInfo` so deep folder nesting never eats the Windows path budget. Contributors run `git config core.longpaths true` once.
- **Soft length cap:** if `{Class}.{Method}` would push a `.verified.txt` filename past ~90 chars, the test scope is too broad — split the test. Never truncate or hash snapshot filenames (they must stay greppable and rename-stable).
- Never commit `.received.*` files — only `.verified.*`.

## What not to do

- Don't test that a method was called — verify outcomes, not interactions.
- Don't use **Moq** or any mocking library for infrastructure boundaries — use real Azurite containers in `Edict.Azure.Tests`.
- Don't mock away streams/stores in `Edict.Azure.Tests`; don't pull Azurite into `Edict.Core.Tests`.
- Don't share mutable state between tests.
- Don't assert on log output or internal exception messages unless the message is part of the public contract.
- **FluentAssertions is banned** (commercial license) — do not add it or a wrapper.
- Don't add section-divider comments inside test files. If you want to separate groups, split into separate files.
- Don't add lines when renaming identifiers in analyzer test fixtures — diagnostic assertions key on line numbers.
