# Conformance axis-split — full partition + bucket 2 (streaming axis)

Recorded for PRD #289, slice #291. Bucket 1 (substrate-independent) is recorded
separately in [`conformance-axis-split-bucket1.md`](conformance-axis-split-bucket1.md)
and already runs once, in-memory, in `Edict.Core.Tests`. This note records the
**complete partition of every remaining scenario** into the streaming axis
(bucket 2), the persistence axis (bucket 3), and the irreducible interaction
residue (bucket 4) — decomposing each interaction scenario into a streaming half
and a persistence half wherever possible — and then builds **bucket 2** end-to-end
on AQS as the tracer-bullet streaming provider.

The partition is a judgement call, so it is recorded here to be reviewable, not
implicit. The rule applied: a scenario lands on the axis whose **assertion** it
makes. Asserts a publish landed, an event was delivered/redelivered, a duplicate
was suppressed → streaming. Asserts a row/state survived, a dead-letter row
landed, a store round-trips → persistence. When a scenario asserts both, it is
split; the half each battery cannot prove alone (the write-fault∧redelivery
conjunction) is the only true bucket-4 residue.

## Bucket 2 — streaming axis (built in this slice)

Real broker + **reference persistence** (Orleans memory grain storage + the
in-memory `ReferenceClaimCheckStore` / `ReferenceTableStoreFactory` in
`Edict.Tests.Conformance`). The reference persistence honours the store
*contracts* (grain-state put/get + reload, claim-check round-trip by `EventId`,
table row read/write) but is **never asserted upon** — a streaming scenario can
only touch what `StreamingConformanceFixture` exposes (`Sender`, `GrainFactory`,
`ClaimCheckBlobExistsAsync`), so it physically cannot assert a real-persistence
property. That structural seam makes the `azure-queue-trace-propagation-gap`
green-and-wrong failure mode impossible by construction.

| Scenario (migrated to `…Conformance.Streaming`) | What it asserts | Decomposition note |
|---|---|---|
| `CommandPipelineEndToEndScenarios` | `Send` routes to handler, returns Accepted/Rejected envelope | Accepted rides the publish path; ref persistence holds idempotency state |
| `AcceptedCommandPublishesEventScenarios` | accepted command publishes its event to the domain stream | pure streaming |
| `RejectedCommandPublishesNoEventScenarios` | rejected command's buffered events are dropped, nothing reaches the stream | pure streaming |
| `EventHandlerHandlesPublishedEventScenarios` | a published handled event runs `Handle` exactly once | pure streaming |
| `EventHandlerDedupsWithinRingScenarios` ⭐ | a redelivered same-`EventId` event is suppressed | **dedup-over-redelivery** — real stream redelivers, ref persistence holds the ring |
| `EventHandlerNoOpForUnhandledTypeScenarios` | an unhandled event type is a pure no-op | streaming delivery |
| `EventHandlerSpanStitchAcrossOutboxHopScenarios` | deferred-invocation span nests under the publish span across the stream hop | streaming + telemetry; trace propagation is substrate-dependent |
| `UnhandledEventTypeRingSlotScenarios` | an unhandled event consumes no dedup-ring slot | streaming delivery + ring; ref persistence |
| `ProjectionDeliveryScenarios` | accepted command delivers its event to the in-memory projection grain | streaming delivery (no store) |
| `ProjectionUnhandledEventScenarios` | unhandled event type is a no-op at the projection grain | streaming delivery |
| `LargePayloadPublishesViaBlobScenarios` | raised event publishes as a pointer envelope; body lands in the claim-check store keyed by `EventId` | claim-check **streaming half** — pointer publish; ref store holds the body, probed via `ClaimCheckBlobExistsAsync` |
| `ReceiverUnwrapsClaimCheckScenarios` | a pointer envelope unwraps to the inner event before `Handle` | claim-check **streaming half** — receiver unwrap; ref store provides the body |
| `ClaimCheckPayloadSizeMetricsScenarios` | the `edict.claim_check.payload.size` metric fires on a spilled raise | claim-check **streaming half** — spill on publish |
| `EventTelemeterizedTagsOnSpansScenarios` | `[EdictTelemeterized]` tags land on both publish and handle spans | streaming + telemetry |
| `SagaSendCommandEffectDeliversScenarios` | a delivered event reaches the saga, which dispatches one command | streaming delivery to the saga; ref persistence holds `Progress` |
| `SagaReceivesClaimCheckScenarios` | a pointer envelope reaches the saga and dispatches its command | claim-check **streaming half** — pointer delivery to the saga |
| `SagaTimeoutCapCompensationScenarios` | a fired absolute-cap dispatches the compensating command (inline + oversized triggers) | trigger delivery is streaming; the cap fires from in-memory reminders; ref persistence holds saga state |
| `RedeliverAfterThrowScenarios` ⭐ NEW | a consumer turn that faults its commit is **redelivered** by the real stream and applied exactly once | **redeliver-after-throw** — first-class streaming scenario (story 9). The fault (`ControllableGrainStorage` over the reference memory store) is only the *trigger*; the assertion is that AQS redelivered. The conjunction with a real store's fault mode is bucket 4. |

`SagaSendCommandEffectDelivers` and `SagaReceivesClaimCheck` also persist saga
`Progress`; that write rides the reference persistence and is not asserted upon —
the saga-progress *survival* contract is the persistence axis's job
(`RingSurvivesDeactivation` shape).

## Bucket 3 — persistence axis (later slice #293)

Dumb reference stream (`MemoryStreams`, deliver-once) + **real DB**. Assertion is
about durable persistence:

| Scenario | What it asserts |
|---|---|
| `OutboxStateAtomicityScenarios` | grain state survives deactivation, proving the atomic `{State, Outbox}` envelope commit |
| `OutboxDrainOnActivationScenarios` | pending outbox drains on reactivation; empty outbox is a no-op |
| `OutboxDrainReminderPeriodScenarios` | a failing drain registers the lazy recovery reminder |
| `OutboxRecoveryAfterCrashScenarios` | a post-commit publish failure recovers via reminder (the pending entry must survive — real persistence) |
| `OutboxHappyPathScenarios` (persistence half) | a successful inline drain leaves zero pending and no reminder (the publish half is covered by `AcceptedCommandPublishesEvent` in bucket 2) |
| `RingSurvivesDeactivationScenarios` | the dedup ring survives grain deactivation |
| `TableProjectionWritesRowScenarios` | a projection write lands a row readable via `IEdictTableRepository<T>` |
| `TableProjectionIncrementsOnSubsequentEventScenarios` | read-modify-write increments the row on a subsequent event |
| `TableProjectionConsumerRowKeyScenarios` | the consumer-specified row key drives the row coordinates |
| `TableProjectionSingletonScenarios` | a singleton projection stores a distinct row per aggregate |
| `TableProjectionReceivesClaimCheckScenarios` (persistence half) | the unwrapped pointer event writes its projection row (the unwrap half is `ReceiverUnwrapsClaimCheck` in bucket 2) |
| `ClaimCheckKeyContractScenarios` | the real store round-trips by `EventId`; a miss throws `EdictClaimCheckFetchException` classifying to `Substrate` |
| `MissingClaimCheckDeadLetterClassificationScenarios` | a missing claim-check dead-letters with the `Substrate` failure reason (the dead-letter row + classification are persistence; the receiver fetch trigger is covered by `ReceiverUnwrapsClaimCheck`) |
| `HandlerFailurePromotesToDeadLetterScenarios` | a handler failure lands a dead-letter row with RCA fields |
| `SagaCoordinationPromotesToDeadLetterScenarios` | a saga-coordination fault names the typed exception on the row |
| `UnregisteredTypePromotesToDeadLetterScenarios` | an unregistered command type names the typed exception on the row |
| `TableBackedDeadLetterRepositoryScenarios` | the dead-letter read seam (`ListAsync`/`ListAllAsync`) honours its contract |
| `SagaTimeoutTerminalDeadLetterScenarios` | a new event at a terminal saga dead-letters (inline + oversized) — the asserted artefact is the dead-letter row |
| `StateWriteFaultScenarios.CommandWriteFault…` | a command-path write fault throws to the sender, drops the dirty activation, applies exactly once on retry (sender retries — no stream redelivery) |
| `DeadLetterPromotionMetricsScenarios` | the `edict.dead_letter.promotion.count` counter fires with the allowlist failure reason |
| `OutboxPendingCountMetricsScenarios` | the `edict.outbox.pending.count` gauge sums across aggregates of the same type |

## Bucket 4 — irreducible interaction (later slice #294)

A handful of tests, not a battery. The only genuine residue is the conjunction a
single-axis battery cannot prove alone, plus the per-pairing boot smoke:

| Case | Why it cannot decompose |
|---|---|
| `StateWriteFaultScenarios.EventConsumerWriteFault…` | the write-fault ∧ redelivery conjunction: a **real store's** write fault drops the dirty activation **and** the **real stream** redelivers, applying exactly once on a clean reload. The streaming half (provider redelivers after a thrown turn) is `RedeliverAfterThrow` in bucket 2; the persistence half (clean reload, no redelivery) rides the persistence battery; only their conjunction needs both real backends at once. |
| Per-pairing composition/wiring smoke | each shipped pairing (AQS×AzureTable, AQS×Postgres, Kafka×Postgres, Kafka×AzureTable) must boot with both real providers' types present and round-trip once — DI + serializer registration, not a behaviour battery. |

## Migration mechanics

The old cross-battery keeps running and stays green; nothing is deleted in this
slice. The bucket-2 scenario classes are **copied** into the
`Edict.Tests.Conformance.Streaming` namespace generic over the new
`StreamingConformanceFixture`, because the originals are still bound by the four
cross-fixtures over `ConformanceFixture` and cannot be re-parented without
breaking the old battery. The shared workload grains/events/commands are reused
as-is (they are substrate-neutral and only need an `"edict"` stream provider,
which the streaming fixture supplies as real AQS). Slice #296 (demolition) deletes
the originals and the duplication in one cut.
