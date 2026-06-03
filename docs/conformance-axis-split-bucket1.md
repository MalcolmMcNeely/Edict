# Conformance axis-split — bucket 1 (substrate-independent)

Recorded for PRD #289, slice #290. The axis-split reclassifies every conformance
scenario into one of four buckets. **Bucket 1** is the substrate-independent set:
behaviours that cannot vary by streaming or persistence provider, so re-proving
them on each real-backend cross-fixture bought cost and no signal. They now run
**once, in-memory**, in `Edict.Core.Tests` (shared `SubstrateIndependentClusterFixture`,
Orleans memory streams + memory grain storage — no Azurite, Postgres, or Kafka).

## The bucket-1 set

| Behaviour | New home in `Edict.Core.Tests` | Deleted per-provider bindings |
|---|---|---|
| Command validation (reject/accept, no-validator dispatch, rejected-span status, grain-state via root context, concurrent no-interleave) | `Commands/CommandValidatorTests` | `AzureClusterFixture`, `PostgresClusterFixture` |
| Saga command-span parentage (command span nests under the saga handle span across the dispatch hop) | `Saga/SagaCommandSpanNestsUnderHandleSpanTests` | `AzureClusterFixture`, `KafkaClusterFixture`, `KafkaAzureClusterFixture`, `PostgresClusterFixture` |
| Dedup span emission (span tagged `deduplicated` on same-grain republish) | `Idempotency/DedupSpanEmissionTests` | `AzureClusterFixture`, `PostgresClusterFixture` |
| Idempotency window-size config read (silo default vs per-grain override) | `Idempotency/IdempotencyWindowSizeTests` | dedicated `IdempotencyWindowSize{Cluster,Postgres}Fixture` |
| In-memory claim-check key contract (put/get by `EventId`, miss → `EdictClaimCheckFetchException` → `Substrate`) | `ClaimCheck/InMemoryClaimCheckKeyContractTests` | (relocated from `Edict.Tests.Conformance`) |

The claim-check **key contract** moves only its in-memory binding. The real-store
bindings (`AzureClaimCheckClusterFixture`, `KafkaBlobClaimCheckClusterFixture`,
`PostgresClaimCheckClusterFixture`) prove the actual backend and belong to the
persistence axis (bucket 3, slice #293) — they and `ClaimCheckKeyContractScenarios`
stay put.

## Boundary note — what was *not* bucket 1

`EventHandlerSpanStitchAcrossOutboxHopScenarios` crosses a real stream hop, so its
trace-propagation is substrate-dependent (the `azure-queue-trace-propagation-gap`
failure mode). It stays in the cross-battery and moves to the streaming axis
(bucket 2), **not** here.

## What this slice did *not* touch

The old cross-battery otherwise stays intact and green. The shared conformance
workloads (`ConformanceWorkload`, `DedupTestWorkload`, `SagaWorkflow`) remain —
other scenarios still use them. Only the bucket-1 abstract bases, the
window-size-exclusive fixtures/probes, and the in-memory claim-check double were
removed. The full demolition of the cross-fixtures lands in slice #296.
