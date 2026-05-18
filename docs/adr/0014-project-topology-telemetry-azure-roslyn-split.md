# Project topology: extract Edict.Telemetry and Edict.Azure; split Edict.Generators / Edict.Analyzers

**Status:** accepted — extends ADR 0008's shared-kernel-vs-runtime split; supersedes ADR 0008's "`Edict.Core` keeps … the validation pipeline / Azure-Table implementation" placement (the Azure move is detailed in ADR 0015)

The two-assembly Contracts/Core split (ADR 0008) is refined into a small set of leaf-respecting assemblies so the dependency graph matches the conceptual seams:

- **`Edict.Telemetry`** — the single `ActivitySource` identity, `ActivitySourceExtensions` (span start) and `ActivityExtensions` (tag writing, replacing the bundled `CommandSpanScope`), **and** the ADR-0003 `RequestContext` stream-hop trace capture. It references `Orleans.Core` (where `RequestContext` lives — client abstractions the command *producer* already has), **not** the Orleans server runtime. It is deliberately *not* a pure leaf: the `Command → Publish → Handle` stitch is one cohesive ADR-0003 mechanism and must not be fragmented across Core/Telemetry just to keep Telemetry dependency-free.
- **`Edict.Azure`** — all Azure-specific implementations (see ADR 0015).
- **`Edict.Generators` + `Edict.Analyzers`** — split into two assemblies, each with its own test project and an ADR-linked `README.md` (analyzers = the `EDICT00x` catalog; generators = trigger + emitted-shape per generator). The ADR-0005 well-known FQN constants — the only thing they share — live in a single `EdictWellKnownNames.cs` `<Compile>`-linked into both (Roslyn assemblies are netstandard2.0 with no deps per ADR 0005, so a shared *runtime* assembly is not an option). "Analyzer" is spelled the American/Roslyn way everywhere (`DiagnosticAnalyzer`, `EDICT00x`).

## Considered Options

- **`RequestContext` capture stays in Core, Telemetry is a pure leaf** — rejected by the author: splitting one trace mechanism across two assemblies for purity's sake fragments ADR 0003; the `Orleans.Core` dependency is client-tier, not the server-runtime leak ADR 0008 guards against.
- **Keep generators+analyzers in one Roslyn assembly** — rejected: they are distinct concerns with distinct test surfaces; one shipping unit is a packaging detail, not a reason to co-mingle source and tests.
- **Duplicate the FQN constants + a parity test** — rejected in favour of one linked source file: same guarantee, fewer moving parts.

## Consequences

- New `BoundaryTests` guardrails: `Edict.Telemetry` must not depend on the Orleans **server** runtime / grain bases / hosting (only `Orleans.Core`); `Edict.Core` must not depend on `Azure.*` (ADR 0015).
- `Edict.Core` slims to the persistence-agnostic grain runtime + producer plumbing; it is re-foldered by concept (`Commands/`, `Projections/`, `Sagas/` future) with the shared `EdictEventDeduplicationGrain` + state under `Dedup/` (it is the shared inheritance root, not an Events concept). The empty `Pipeline/.gitkeep` and `Validation/.gitkeep` placeholders are deleted; `EdictValidationKeys` moves to `Commands/`.
- Consumers reference one Roslyn package that ships both analyzer DLLs; the source split is internal to Edict.
- ADR 0008's tested invariants (Contracts has no Orleans; the pure command-def assembly has no grain bases) are unchanged and still pass.
