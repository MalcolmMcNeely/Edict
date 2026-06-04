# Conformance axis-split — bucket 3 (persistence axis)

Recorded for PRD #289, slice #293. The full four-bucket partition of every
remaining conformance scenario is recorded in
[`conformance-axis-split-bucket2.md`](conformance-axis-split-bucket2.md) (bucket 1
— substrate-independent — is in
[`conformance-axis-split-bucket1.md`](conformance-axis-split-bucket1.md)). This
note records **bucket 3** built end-to-end on **Azure Table/Blob** as the
tracer-bullet persistence provider.

## Shape

Dumb deliver-once reference stream (Orleans `MemoryStreams` — delivers each
publish once to its implicit subscribers, carries the `EventId` intact, **never
redelivers**) + **real persistence** (Azure Blob grain storage, the Azure Table
read/write store, the literal dead-letter table, and a real Azure Blob
claim-check store). The assertion is always about durable persistence — a row or
state survived, a dead-letter row landed, a store round-trips.

`PersistenceConformanceFixture` (in `Edict.Tests.Conformance`) exposes only
`Sender`, `GrainFactory`, `GetTableRepository<T>`, and `TableStoreFactory`. A
persistence scenario can only reach those seams, so it physically **cannot**
assert an ordering, partitioning, trace-propagation, or transport-timing
property of the stream — making the `azure-queue-trace-propagation-gap`
green-and-wrong failure mode impossible by construction. The reference stream is
never asserted upon.

## Reference-fidelity contract

The reference stream is `MemoryStreams`-based and carries the `EventId` intact;
it is **never** asserted on for any streaming property. The structural seam (the
fixture surface) enforces this — there is no streaming probe to call. Where a
scenario's streaming half exists (claim-check unwrap, redelivery), that half is
proven on the streaming axis (bucket 2); only the persistence half rides here.

## The bucket-3 set (built in this slice, bound on Azure Table/Blob)

| Scenario (in `…Conformance.Persistence`) | What it asserts | Fixture |
|---|---|---|
| `OutboxStateAtomicityScenarios` | grain state survives deactivation, proving the atomic `{State, Outbox}` envelope commit | default |
| `OutboxHappyPathScenarios` | a successful inline drain leaves zero pending and no reminder; a batch loses no events | default |
| `OutboxDrainOnActivationScenarios` | pending outbox drains on reactivation; empty outbox is a no-op | controllable-executor |
| `OutboxDrainReminderPeriodScenarios` | a failing drain registers the lazy recovery reminder | controllable-executor |
| `OutboxRecoveryAfterCrashScenarios` | a post-commit publish failure recovers via reminder — the pending entry survives on real persistence | controllable-executor |
| `RingSurvivesDeactivationScenarios` | the dedup ring survives grain deactivation | default |
| `TableProjectionWritesRowScenarios` | a projection write lands a row readable via `IEdictTableRepository<T>` | default |
| `TableProjectionIncrementsOnSubsequentEventScenarios` | read-modify-write increments the row on a subsequent event | default |
| `TableProjectionConsumerRowKeyScenarios` | the consumer-specified row key drives the row coordinates | default |
| `TableProjectionSingletonScenarios` | a singleton projection stores a distinct row per aggregate | default |
| `TableProjectionReceivesClaimCheckScenarios` (persistence half) | the unwrapped pointer event writes its projection row | claim-check (1-byte) |
| `ClaimCheckKeyContractScenarios` | the real store round-trips by `EventId`; a miss throws `EdictClaimCheckFetchException` classifying to `Substrate` | claim-check (1-byte) |
| `MissingClaimCheckDeadLetterClassificationScenarios` | a missing claim-check dead-letters with the `Substrate` failure reason | blob-missing |
| `HandlerFailurePromotesToDeadLetterScenarios` | a poisoned outbox entry lands a dead-letter row with RCA fields | dead-letter (max-attempts 2) |
| `SagaCoordinationPromotesToDeadLetterScenarios` | a saga-coordination fault names the typed exception on the row | dead-letter (max-attempts 2) |
| `UnregisteredTypePromotesToDeadLetterScenarios` | an unregistered event type names the typed exception on the row | dead-letter (max-attempts 2) |
| `TableBackedDeadLetterRepositoryScenarios` | the dead-letter read seam (`ListAsync`/`ListAllAsync`) honours its contract | default |
| `SagaTimeoutTerminalDeadLetterScenarios` | a new event at a terminal saga dead-letters (inline + oversized) — the asserted artefact is the dead-letter row | default |
| `StateWriteFaultScenarios.CommandWriteFault…` | a command-path write fault throws to the sender, drops the dirty activation, and applies exactly once on retry (sender retries — no stream redelivery) | state-write-fault |
| `DeadLetterPromotionMetricsScenarios` | `edict.dead_letter.promotion.count` fires with the allowlist failure reason | dead-letter (max-attempts 2) |
| `OutboxPendingCountMetricsScenarios` | `edict.outbox.pending.count` sums across aggregates of the same type | dead-letter (max-attempts 2) |

`StateWriteFaultScenarios` carries only the command-path half here. Its
consumer-side `EventConsumerWriteFault…` fact is the write-fault ∧ redelivery
conjunction — bucket 4 (slice #294) — and is **not** in this battery.

## Fixtures

The Azure binding lives in the new `Edict.Azure.Persistence.Tests` project. One
parameterised `AzurePersistenceFixtureBase` stands up every shape (dumb
`MemoryStreams` + real Azure Table/Blob); concrete fixtures pick the knobs:

| Fixture | Knobs |
|---|---|
| `AzurePersistenceFixture` | shipped defaults, real publish executor |
| `AzurePersistenceControllableExecutorFixture` | controllable publish executor; 200 ms base delay, no jitter, 2 min reminder period |
| `AzurePersistenceDeadLetterFixture` | controllable publish executor; `OutboxMaxAttempts` = 2 |
| `AzurePersistenceStateWriteFaultFixture` | `ControllableGrainStorage` over `edict-state` |
| `AzurePersistenceClaimCheckFixture` | 1-byte claim-check threshold; forwards `IClaimCheckStoreFixture` |
| `AzurePersistenceBlobMissingFixture` | `OutboxMaxAttempts` = 3, tight backoff |

`xunit.runner.json` runs collections serially because the per-shape fixtures each
stand up an Orleans cluster on a single host and the silo-gateway bring-up races
under that contention. The `ControllableOutboxExecutor` / `ControllableGrainStorage`
fault switches are per-fixture instances (each fixture owns its own and wires it
into its silo), so they are not a reason to serialise.

## The `Edict.Azure.Tests` split + SDK note

`Edict.Azure.Persistence.Tests` is the persistence side of the test split that
mirrors the ADR-0042 production assembly split (the streaming side,
`Edict.Azure.Streaming.Tests`, landed in slice #291). It references the
`Edict.Azure.Persistence` package for grain storage + table store.

It also references `Edict.Azure.Streaming` for the **blob-backed claim-check
store**. ADR-0042 deliberately keeps `AzureBlobClaimCheckStore` in
`Edict.Azure.Streaming` (claim-check rides with streaming because AQS's 64 KB cap
makes it operationally required there) and explicitly **rejected** moving it under
`.Persistence`. Rather than reverse that decision for a test-axis need, the
persistence battery wires the existing store through the public
`IEdictClaimCheckStore` seam — the very path ADR-0042 sanctions for a consumer who
wants Azure-blob claim-check without the AQS stream. The trade-off is that this
test project carries the streaming SDK transitively; the production split is
untouched.

## Migration mechanics

The old cross-battery keeps running and stays green; nothing is deleted in this
slice. The bucket-3 scenario classes are **copied** into the
`Edict.Tests.Conformance.Persistence` namespace generic over the new
`PersistenceConformanceFixture`, because the originals are still bound by the
four cross-fixtures over `ConformanceFixture`. `ClaimCheckKeyContractScenarios`
is the exception — it is already generic over the narrow `IClaimCheckStoreFixture`
seam, so the persistence binding reuses it in place. The shared workload
grains/events/commands and the controllable test doubles are reused as-is. Slice
#296 (demolition) deletes the originals and the duplication in one cut.
