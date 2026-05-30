# Edict.Azure split into Streaming and Persistence packages

Edict ships as separate NuGet packages so a consumer pulls only the substrate it uses. ADR-0012 drew the Azure boundary at one assembly — `Edict.Azure` owns *all* Azure-specific implementations. With four valid consumer scenarios (AQS + Azure-persistence, AQS + Postgres, Kafka + Azure-persistence, Kafka + Postgres), the single-assembly shape forces two of those four to drag an Azure SDK they never call. The boundary is redrawn at the substrate-concern level: `Edict.Azure.Streaming` carries the Azure Queue Storage stream provider and the blob claim-check store; `Edict.Azure.Persistence` carries the Azure Table read/write store, the Orleans grain-persistence binding, and the reminders binding. This supersedes the "Edict.Azure owns all Azure-specific implementations" line in ADR-0012 — the cohesion principle moves from "one cloud, one package" to "one substrate concern, one package."

Claim-check rides with `Edict.Azure.Streaming` because Azure Queue Storage's 64 KB payload cap makes a claim-check store operationally required for AQS users, while a Kafka consumer who wants Azure-blob claim-check still has the `IEdictClaimCheckStore` seam to wire one up directly. Bundling claim-check with `.Persistence` would couple event-payload sizing to grain state — two unrelated concerns.

`Edict.Postgres` stays a single package: persistence, table-storage, and Postgres-backed claim-check all sit on `Npgsql`, so splitting it sheds no transitive dependency and only fragments the ADR-0029 boundary for symmetry's sake.

## Considered Options

- **Keep `Edict.Azure` single** — rejected: two of the four consumer scenarios pull an unused ~600 KB Azure SDK, and a later split would require deprecating the original ID on nuget.org. Pre-1.0 still doesn't make a package-ID rename free.
- **Three-way Azure split (`Streaming` + `Persistence` + `ClaimCheck`)** — rejected: `AzureBlobClaimCheckStore` is a single ~2.3 KB file with no independent consumer story; a separate package would exist purely for mechanical SDK-independence, not consumer benefit.
- **Two-way split with claim-check under `Persistence`** — rejected: claim-check exists to dodge stream-payload caps, not to durably store grain state; pairing it with grain persistence muddles the concern.
- **Mirror the split on `Edict.Postgres`** — rejected: every sub-area pulls Npgsql, so the split sheds no dependency and adds two csprojs plus an ADR for symmetry alone.

## Consequences

- `Edict.Azure` retires; `Edict.Azure.Streaming` and `Edict.Azure.Persistence` replace it as two `net10.0` assemblies under the same folder layout.
- `EdictAzureSiloBuilderExtensions` splits into two extension surfaces (`AddEdictAzureStreaming` / `AddEdictAzurePersistence` or similar) — each lives next to the assembly it configures.
- `Edict.Substrate.Azurite` and the Azure conformance suite take `ProjectReference`s on both new assemblies.
- The Sample app's Azure-flavoured silo rewires onto both extension methods.
- Architecture-tests' dependency assertions rebase: the "no `Azure.*` reference outside Azure assemblies" rule now lists both Azure assemblies as permitted carriers.
- ADR-0012's `Edict.Azure` line is superseded; ADR-0012 itself is annotated to point at this ADR for the Azure boundary, and its other clauses (Telemetry, Generators/Analyzers, foldering) remain in force.
- First NuGet publish ships the right package IDs from `0.1.0` — no later deprecation of `Edict.Azure`.
