# Theorycraft: Mutation testing (Stryker.NET) on Edict.Generators & Edict.Analyzers

## What this is

Exploratory, not committed work. We scoped what it would take to point **Stryker.NET** at
the two highest-value silent-failure targets in the repo: `Edict.Generators` and
`Edict.Analyzers`. This doc captures the feasibility findings so a future session can decide
whether to actually do it. Nothing was installed, configured, or committed.

## Why these two projects

Source-gen/analyzer bugs **fail silently**: a wrong skip drops a consumer to the slow path
with no error; an analyzer that stops firing produces no diagnostic. Line coverage proves a
line *ran*; it can't prove an assertion *constrains* behaviour. Mutation testing exposes
exactly this gap: surviving mutants = behaviour your tests execute but never pin. These two
projects are the single best place in the repo to spend a mutation-testing budget.

## Feasibility verdict: GREEN

The decisive question for any Roslyn-component mutation run is **how the test project
references the component**. If it's referenced as an analyzer (`OutputItemType="Analyzer"`,
`ReferenceOutputAssembly="false"`), Stryker mutates the DLL but the compiler pipeline never
loads the mutant, so every mutant "survives" and the score is meaningless.

Both test projects instead reference the component as a plain `ProjectReference` with
`ReferenceOutputAssembly="true"`, and the harnesses drive it in-process:
- `Edict.Generators.Tests` runs `CSharpGeneratorDriver.RunGenerators()` via `GeneratorTestHarness.cs`
- `Edict.Analyzers.Tests` runs `compilation.WithAnalyzers(...).GetAnalyzerDiagnosticsAsync()` via `AnalyzerTestHarness.cs`

A mutated build is therefore picked up by ordinary assembly resolution. This is the shape
Stryker needs.

## Key facts (so the next session doesn't re-investigate)

| Item | Value |
|---|---|
| Solution | `Edict/Edict.slnx` (modern `.slnx`, not legacy `.sln`) |
| `Edict.Generators.csproj` | `netstandard2.0`; `IncludeBuildOutput=false`; `Microsoft.CodeAnalysis.CSharp` 5.0.0 (PrivateAssets=all) |
| `Edict.Analyzers.csproj` | `netstandard2.0`; `IncludeBuildOutput=false`; CodeAnalysis.CSharp + .Workspaces 5.0.0; compile-links `EdictWellKnownNames.cs` from Edict.Generators |
| Generators test proj | `net10.0`, xUnit + Verify.Xunit; ~15 files / ~60 tests; `ReferenceOutputAssembly="true"` |
| Analyzers test proj | `net10.0`, xUnit; ~30 files / ~88 tests; `ReferenceOutputAssembly="true"`; CodeAnalysis VersionOverride 5.3.0 |
| Existing Stryker config | None — no `stryker-config.json`, no `dotnet-stryker` in any tool manifest, no "stryker" string anywhere. Clean slate. |

Exact reference lines confirmed:
- `Edict.Generators.Tests/Edict.Generators.Tests.csproj:10`
  `<ProjectReference Include="..\Edict.Generators\Edict.Generators.csproj" ReferenceOutputAssembly="true" />`
- `Edict.Analyzers.Tests/Edict.Analyzers.Tests.csproj:9`
  `<ProjectReference Include="..\Edict.Analyzers\Edict.Analyzers.csproj" ReferenceOutputAssembly="true" />`

## Three friction points to watch (ranked)

1. **`.slnx` support.** Stryker historically keyed off `.sln`. Newer versions parse `.slnx`,
   but verify the installed version does. Safer default: don't set the `solution` key at all,
   point `project` + `test-projects` directly (configs below already do this).
2. **netstandard2.0 to net10.0 cross-TFM.** Stryker rebuilds the netstandard2.0 assembly with
   mutations; the net10.0 test project consumes it. Standard cross-TFM ProjectReference, but
   this is the first place to look if a run errors at the build step.
3. **`EdictWellKnownNames.cs` compile-link.** `Edict.Analyzers` compile-links that file from
   `Edict.Generators`. Stryker mutates only the project-under-test's compilation, so a
   well-known-name mutation only exercises the half the current target owns. Not a blocker,
   just don't read the two projects' scores as fully independent on that shared file.

## Proposed first move (not yet done)

Pin the tool in a manifest (matches repo habit):
```
dotnet new tool-manifest
dotnet tool install dotnet-stryker
```

Two configs, one per target (Stryker runs one project-under-test at a time):
```json
// stryker-config.generators.json
{ "stryker-config": {
  "project": "Edict.Generators.csproj",
  "test-projects": ["Edict.Generators.Tests/Edict.Generators.Tests.csproj"]
} }
```
```json
// stryker-config.analyzers.json
{ "stryker-config": {
  "project": "Edict.Analyzers.csproj",
  "test-projects": ["Edict.Analyzers.Tests/Edict.Analyzers.Tests.csproj"]
} }
```
Run: `dotnet stryker -f stryker-config.generators.json` (then analyzers).

**Expect a slow first run.** ~148 tests re-run per relevant mutant; generators/analyzers have
lots of mutable surface (string constants, syntax-kind conditionals, equality checks). Lean on
default coverage-based mutant filtering + `--concurrency`. Kick it off manually, don't block a
session on it.

## What success looks like / what to hunt in the report

Surviving mutants are the deliverable. Specifically expect them to expose:
- Analyzer diagnostics whose **trigger condition** is executed but never asserted (flip a `==`
  in a syntax check, no test notices, that guard is under-pinned).
- Generator branches where **emitted output** isn't snapshot-asserted tightly enough: a
  mutated branch still passes Verify.
- `EquatableArray<T>` / incrementality equality logic (ADR work in PRD #161, Slice B #163):
  classic "covered but a mutated `Equals` survives" code.

Triage note: **equivalent mutants** (mutations that can't change observable behaviour) are
expected false positives. They cannot be killed and must be dismissed by hand, not chased.

## Open decisions for the next session

- Is this worth committing as standing tooling (configs + CI gate with a mutation-score
  threshold), or a one-off diagnostic to find test gaps and then discard? Lean: one-off first,
  read the report, then decide on a gate.
- If gaps are found, the fix is new/strengthened tests in the existing harnesses, a `tdd` +
  `testing`-skill task, separate from standing up Stryker.

## Suggested skills for next session
- **testing** — when turning surviving mutants into strengthened tests (ADR-0016 layering, Verify rules).
- **tdd** — red-green loop for the specific gaps Stryker surfaces.
- **surface-config** — only if a mutation-score threshold/CI gate becomes a tunable knob (unlikely this round).
