# Lockstep prerelease versioning, tag-driven publish

Edict's first public NuGet release covers eight packages (`Edict.Contracts`, `Edict.Telemetry`, `Edict.Core`, `Edict.Azure.Streaming`, `Edict.Azure.Persistence`, `Edict.Kafka`, `Edict.Postgres`, `Edict.Testing`). The framework is portfolio-grade and has never been exercised against real workloads outside Testcontainers; there are no released consumers. Every choice in this ADR rides on those two facts.

All packages share one version line, bumped in lockstep. The version sits at `0.x.y-preview.N` until the API is stable enough to drop the suffix — the `-preview` segment trips the NuGet UI's prerelease gate so a consumer who only scans the leading `0` still has to opt in explicitly to install. The version is read by `MinVer` from a `v*` git tag on the published commit — no `<Version>` in `Directory.Build.props`, no hand-bump, no `version.json` commit-height stamp. The publish workflow is triggered by `on: push: tags: ['v*']` — tag push is the only path to nuget.org, and the same workflow creates a GitHub Release with the `.nupkg` and `.snupkg` files attached and notes auto-generated from commits since the previous tag.

Lockstep matches the convention of every comparable .NET framework family (Orleans, Aspire, ASP.NET Core), keeps the inter-`Edict.*` package version constraints trivial (every Edict package depends on the *same* version of its Edict siblings), and means a consumer reading "I'm on `Edict.Kafka 0.1.0-preview.3`" knows every other Edict reference in their graph matches.

## Considered Options

- **`0.x.y` plain (no suffix)** — rejected: doesn't engage NuGet's prerelease gate; consumers who only scan the leading `0` misread it as "stable enough to depend on." The suffix is a one-character cost for an explicit opt-in.
- **`0.x.y-alpha.N`** — rejected: "alpha" reads as "expected to break"; "preview" matches the .NET-ecosystem convention Microsoft uses for Orleans, Aspire, .NET previews. Same NuGet behaviour, better signalling.
- **`0.0.x` patch-only** — rejected: signals "spike-quality" rather than "pre-1.0 framework"; the framework is more developed than that.
- **Independent versions per package** — rejected: floating ranges between Edict packages don't compose well under 0.x SemVer, every release would force a per-package "did this change?" decision, and the consumer mental model fragments. The cost of bumping unchanged packages is zero (no bandwidth concern); the cost of independent versioning is the lifetime of the framework.
- **`workflow_dispatch` with a typed version input** — rejected: splits source-of-truth from the tag — the workflow could publish a version that doesn't match the git tag, or no tag at all. Tag push as the trigger makes the tag the version by construction.
- **`Nerdbank.GitVersioning` with commit-height stamping** — rejected: commit-height is the right answer for nightly / continuous-pre-release builds, not for hand-cut tagged releases.
- **Hand-edited `<Version>` in `Directory.Build.props`** — rejected: easy to forget, bumps clutter every PR diff, and the build-time version no longer matches the git tag if either drifts.
- **Trust prior CI for the release gate (pack + push only in the publish workflow)** — rejected: a tag can land on a commit that ordinary CI never validated. The publish workflow re-runs the full suite minus `Edict.Benchmarks.Throughput.Tests` (excluded for runtime and flake reasons per the bench-not-by-default convention).

## Consequences

- A `v*` tag push is the only release path. There is no "republish without retagging" — if a push fails, fix and retag.
- The publish workflow runs the full test suite minus `Edict.Benchmarks.Throughput.Tests` before packing.
- `actions/checkout` uses `fetch-depth: 0` so MinVer can see the tag history.
- Every Edict package moves to the new version every release, including packages with no diff since the last tag — accepted.
- Inter-package `PackageReference` constraints can be expressed as the same single version literal in every csproj.
- Promotion to a stable `0.1.0` is a tag without the `-preview` suffix; no other change.
- `1.0.0` is a deliberate, separately-decided event — there is no calendar trigger.
- The NuGet API key is held on a GitHub Environment named `nuget`, scoped to glob `Edict.*` so first-publish of new package IDs is permitted.
