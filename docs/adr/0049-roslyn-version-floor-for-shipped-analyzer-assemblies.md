# Roslyn version floor for shipped analyzer assemblies

Edict ships two Roslyn-hosted assemblies inside the `Edict.Core` nupkg under `analyzers/dotnet/cs/`: `Edict.Analyzers` (the `EDICT00x` diagnostic surface) and `Edict.Generators` (the unified source generator). At consumer build time the host C# compiler — not the analyzer's compile-time reference — loads those DLLs and binds them against the Roslyn version the consumer's SDK shipped. If a shipped analyzer was compiled against a Roslyn API newer than the consumer's compiler exposes, the load silently degrades: missing diagnostics, missing generated files, no error message at the consumer's build console. The breakage looks like a code bug, not a packaging bug, and the consumer has no signal pointing at the version skew.

This ADR pins the rule. **Shipped analyzer and generator assemblies (`Edict.Analyzers`, `Edict.Generators`) reference `Microsoft.CodeAnalysis.CSharp` (and any `Microsoft.CodeAnalysis.*` companion) at the lowest version any supported consumer compiler exposes — currently `5.0.0`.** That version is the floor; bumping it without checking the corresponding SDK minimum on the consumer side silently raises Edict's consumer-SDK requirement, breaking diagnostic delivery for any consumer on an older SDK.

`Microsoft.CodeAnalysis.CSharp` `5.0.0` is the default in `Directory.Packages.props`. Four projects use newer Roslyn APIs and opt up via `VersionOverride="5.3.0"` at their own `<PackageReference>` site, making the exception visible at the consumer of the package rather than hidden in a centralised version table:

- **`Edict.Mcp`** — uses `Microsoft.CodeAnalysis.Workspaces.MSBuild` to load a consumer's solution by csproj path. Workspaces.MSBuild requires the 5.3.0 line of Workspaces APIs. `Edict.Mcp` is a `dotnet tool`, not a shipped analyzer, so the consumer-compiler load constraint does not apply — it runs on its own SDK.
- **`Edict.Mcp.Tests`** — exercises `Edict.Mcp` against fabricated workspaces; matches the project under test.
- **`Edict.Analyzers.Tests`** — uses `Microsoft.CodeAnalysis.CSharp` + `Microsoft.CodeAnalysis.CSharp.Workspaces` at 5.3.0 to drive the analyzer test harness. The harness runs in-process during `dotnet test`; it is not loaded by a consumer compiler.
- **`Sample.Azure.Silo.Tests`** — uses 5.3.0 Roslyn APIs to assert generator output shape in the Sample's own test surface. Same in-process rationale.

The floor is not encoded in a separate test. The default `<PackageVersion>` in `Directory.Packages.props` plus the four `VersionOverride` sites are the visible record; the ADR is the rationale that survives without csproj comments to carry it.

## Considered Options

- **Status quo: literal `Version="5.0.0"` / `Version="5.3.0"` per csproj.** Worked, but the rationale lived only in csproj comments that lose their structural anchor under Central Package Management — the `Version=` attribute they explained is gone. A future maintainer bumping `Microsoft.CodeAnalysis.CSharp` in `Edict.Analyzers` "because 5.3.0 is newer" cannot be stopped by the conventions on disk; the ADR is the durable artefact that names the rule.

- **Pin every Roslyn reference at 5.3.0.** Rejected. The shipped analyzer assemblies must load on the lowest consumer SDK Edict supports; raising the floor to 5.3.0 raises the consumer's minimum SDK in lockstep, with no signal to the consumer that they need to bump. The floor is a consumer-compatibility decision, not an internal-dependency decision.

- **A repo architecture test asserting "shipped analyzer csprojs reference Roslyn at exactly 5.0.0."** Considered and rejected for portfolio-repo scale. The default in `Directory.Packages.props` and the per-csproj `VersionOverride` sites make the floor visible at the affected csprojs; an extra test is paranoia at this scale. A larger team with multiple regular contributors would justify the guard.

## Consequences

- `Directory.Packages.props` declares `<PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" />` as the default. `Microsoft.CodeAnalysis.CSharp.Workspaces` shares the floor for the same reason and defaults to `5.0.0`.
- `Edict.Mcp`, `Edict.Mcp.Tests`, `Edict.Analyzers.Tests`, and `Sample.Azure.Silo.Tests` apply `VersionOverride="5.3.0"` at their `Microsoft.CodeAnalysis.CSharp` `<PackageReference>` (and at `Microsoft.CodeAnalysis.CSharp.Workspaces` where they reference it). Each override site stands on its own as a visible exception.
- `Microsoft.CodeAnalysis.Workspaces.MSBuild` is only referenced by `Edict.Mcp` and only at `5.3.0`; it has no floor obligation and ships at `5.3.0` in `Directory.Packages.props`.
- A future bump of the analyzer's Roslyn version must be a deliberate decision: raise the floor in `Directory.Packages.props`, confirm the corresponding minimum consumer SDK, and update this ADR. The ADR existing is the gate against accidental drift.
