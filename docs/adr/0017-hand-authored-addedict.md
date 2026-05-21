# Hand-authored `AddEdict` with generator-emitted route registrars

`AddEdict` is hand-authored in `Edict.Core` (discoverable the moment the package is referenced — F12-navigable, IntelliSense-visible) rather than fully generated into a fabricated `Edict.Generated` namespace that has no source-visible existence before a successful build. The generator emits *only* the per-assembly route-table contribution — an `internal static class EdictRouteRegistrar` with a `Register(...)` method and an `[assembly: EdictRoutes(typeof(EdictRouteRegistrar))]` annotation — and `AddEdict` scans assemblies for the attribute to build a fresh route dictionary per call (with a `params Assembly[]` overload as the deterministic escape hatch for test contexts and plugin scenarios). Empty handler set is legal (warning, not throw — the first `Send` will throw `UnroutableCommandException`); duplicate command across assemblies throws at the `AddEdict` call naming both assemblies (the cross-assembly equivalent of `DuplicateCommandRouteAnalyzer`).

## Considered Options

- **Module initializer + static `EdictRouteRegistry`** — rejected: a process-shared static registry leaks routes across `Edict.Testing`'s many `EdictTestApp` instances in one xunit process, undermining test repeatability; also creates a load-order coupling where routes only appear once the CLR touches the handler-bearing assembly.
- **Explicit assembly marker as the only shape** — rejected: two lines for every consumer to cover an edge case most don't have; kept as the `params Assembly[]` overload, not as the primary shape.
- **Cosmetic stub `AddEdict` that throws "did you build?"** — rejected: IntelliSense lights up but the API is a lie until the generator runs.
