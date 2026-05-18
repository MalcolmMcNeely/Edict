# Provider-scoped test layering: Core.Tests is provider-independent; provider suites own the mechanism battery

**Status:** accepted — supersedes the CLAUDE.md mandate "Edict's own tests use the Orleans TestCluster against Azurite via Testcontainers (real at-least-once redelivery)" as the *blanket* rule; the `edict-test-cluster-wiring` auto-memory is updated

Every `Edict.Core.Tests` test currently pays the Azurite/Testcontainers cost and re-proves Azure behaviour other tests already own — the duplication this restructure exists to remove. Tests are re-layered by *what they prove*:

- **`Edict.Core.Tests`** — mechanism *logic* that needs no real backend: dedup-ring semantics, projection load/apply/writeback orchestration, command routing, the validation pipeline. Runs on in-memory streams/stores, no Testcontainers — the fast inner loop.
- **`Edict.Azure.Tests`** — the **full mechanism battery** against real Azurite via Testcontainers: at-least-once queue **redelivery + dedup realism** (the ADR-0002 proof, which *moves here* from Core.Tests) *and* table-projection persistence. This is the provider conformance + integration-realism suite.
- A future **`Edict.DynamoDB.Tests`** re-proves the *same* mechanisms against DynamoDB. The shared, provider-parameterized conformance kit is agreed **in principle**; its concrete shape is **deferred** until `Edict.Azure.Tests` is rebuilt and the real fixture seams are known (avoid designing the seam before one concrete provider proves it).
- Generator/analyzer tests live in their own projects (ADR 0014); span-tree (`ActivityListener`) tests live in `Edict.Telemetry.Tests`. Scale/soak evidence is a separate, explicitly-tagged suite, out of the inner loop.

## Considered Options

- **Keep Azurite broadly in Core.Tests** — rejected: maximal realism everywhere, but every Core test pays Testcontainers cost and re-proves Azure table behaviour `Edict.Azure.Tests` owns.
- **One `Edict.Integration.Tests` for all Azurite/cluster tests** — rejected: mixes the Core dedup proof with Azure persistence proof in one project, losing the per-mechanism, per-provider ownership the split is for.
- **Build the shared conformance kit now** — deferred: the parameterized seam is real but speculative until one provider suite exists to shape it.

## Consequences

- The ADR-0002 at-least-once/dedup realism proof is owned by `Edict.Azure.Tests` (and any future provider suite), not `Edict.Core.Tests`. CLAUDE.md's testing section and the `edict-test-cluster-wiring` memory are rewritten to scope the Azurite mandate to provider suites.
- Adding a provider later = implement its fixture + run the (eventual) shared kit; the kit's absence today is a documented, intentional deferral, not an oversight.
- `Edict.Core.Tests` becoming Testcontainers-free is the explicit signal that a test there is provider-independent; reaching for Azurite in Core.Tests is now a smell.
