# Hand-authored `AddEdict()` with generator-emitted route registrars

**Status:** accepted — clarifies (does not supersede) ADR 0005; introduces a new precedent for generator-emitted attribute application that CONTEXT.md's brand-rule clause (a) is amended to cover.

`AddEdict()` is the DI entrypoint every consumer calls in `Program.cs`. Today it is **fully generated** by `EdictCommandGenerator` into a fabricated `Edict.Generated` namespace, so it has **no source-visible existence before a successful build**: a freshly-onboarded consumer who references the package gets no IntelliSense, no F12 target, and no obvious place to start. The contrast inside our own codebase is sharp — `AddEdictOutbox()` is hand-authored in `Edict.Core.Outbox` and is discoverable the moment the package is referenced. `AddEdict()` is not. We classify this as **originating codegen** (a symbol with no source file anywhere) and treat it as a discoverability bug worth fixing. The framework's other generators emit **enriching codegen** (partials that attach to a type the consumer literally typed) and stay as-is — the consumer's `partial class` declaration is itself the signal that something else completes the type.

We **hand-author `AddEdict()` in `Edict.Core`** and have the generator emit *only* the route-table contribution per consumer assembly: an `internal static class Edict.Generated.EdictRouteRegistrar` with a `Register(Dictionary<Type, CommandRoute>)` method, and an `[assembly: EdictRoutes(typeof(EdictRouteRegistrar))]` annotation pointing the runtime at it. `AddEdict()` scans assemblies for the attribute and invokes each registrar to build a fresh route dictionary per call. A `params Assembly[]` overload is shipped alongside the zero-arg form as the deterministic escape hatch for test contexts and plugin scenarios. The new `EdictRoutesAttribute` lives in `Edict.Contracts` alongside `[EdictRouteKey]` / `[EdictStream]` / `[EdictTelemeterized]` and is brand-prefixed (it is generator-emitted onto the consumer's assembly, which the brand rule now explicitly covers).

## Considered Options

- **Module initializer + static `EdictRouteRegistry`** (no reflection, generator-pure) — rejected on test-surface grounds. `Edict.Testing` runs many `EdictTestApp`s in one xunit process and chaos-default-ON tests stack instances; a process-shared static registry would leak routes across test apps and undermine the test framework's repeatability. It also creates a load-order coupling: routes only appear once the CLR touches the handler-bearing assembly, so calling `AddEdict()` before any reference to `OrderCommandHandler.Assembly` would silently produce an empty route table.
- **Explicit assembly marker as the only shape** (`services.AddEdict().AddEdictRoutesFrom<TMarker>()`) — rejected. Two lines, every consumer pays for an edge case most don't have. We keep this capability via the `params Assembly[]` overload without taxing the happy path.
- **Cosmetic stub `AddEdict()` that throws "did you build?"** — rejected. Doesn't actually solve the discoverability hole; IntelliSense lights up but the API is a lie until the generator runs.
- **Keep fully-generated `AddEdict()` and document loudly** — rejected. The README already documents it; the problem is structural, not informational.

## Consequences

- `EdictCommandGenerator` no longer emits an `AddEdict()` extension or the `Edict.Generated.EdictServiceCollectionExtensions` type. It emits the internal `EdictRouteRegistrar` and the `[assembly: EdictRoutes]` annotation instead.
- Every existing `using Edict.Generated;` line in consumer/sample/test code becomes unnecessary and is removed; `AddEdict()` resolves from `Edict.Core`.
- `EdictRoutesAttribute` is new public API in `Edict.Contracts`. Payload is a single `Type RegistrarType`; no Core dependency.
- `AddEdict()` runtime behaviour:
  - **Zero routes discovered** — log a warning, do not throw. Empty handler set is legal during staged adoption; the first `Send()` will throw `UnroutableCommandException` with the existing clear message, which is the right place to fail loudly.
  - **Duplicate command type across assemblies** — throw at `AddEdict()` call naming both assemblies and the conflicting command. Same severity as `DuplicateCommandRouteAnalyzer`'s single-compilation diagnostic; this is the cross-assembly equivalent the analyzer cannot see.
  - **Assembly passed to `params` overload without `[EdictRoutes]`** — throw. Explicit assemblies imply expected contribution; silent skip is a footgun.
- Brand rule clause (a) is amended in CONTEXT.md to read "a consumer *or its generator-emitted code* types it" — `[EdictRoutes]` is the first precedent for a brand-prefixed attribute that no consumer human writes but the generator applies on their behalf.
- ADR 0005 is unchanged in substance: the generator still references no Edict assembly and matches by FQN. The hand-written shell living in `Edict.Core` is not a generator-side coupling; it's a runtime-side one, which ADR 0005 was never about.
