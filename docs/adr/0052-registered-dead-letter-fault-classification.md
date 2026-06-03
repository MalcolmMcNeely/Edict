# Dead-letter fault classification is a registered per-substrate extension point

When a permanently failing effect is dead-lettered, the promoter tags the `edict.dead_letter.failure_reason` metric with one of a closed allow-list of buckets (`Timeout`, `Substrate`, `Serialization`, `Wiring`, …) so the dimension stays bounded (ADR-0039). Framework causes — BCL exceptions, the `Edict*` typed throws, the claim-check fetch reasons — are classified by a privileged switch in `Edict.Core`. Substrate **driver** faults (a dropped Postgres connection, an Azure `RequestFailedException`, a Kafka `KafkaException`) are classified by an `IDeadLetterFaultClassifier` each provider registers, consulted only when no framework cause matched. `Edict.Core` no longer name-matches any provider.

## The problem

`DeadLetterFailureClassifier` string-sniffed `"Npgsql"`/`"Postgres"` in the exception type name (`IsPostgresDriverFault`) because `Edict.Core` cannot reference Npgsql. This is the single clearest falsifier of the README's "no framework changes needed" claim for a new substrate: every future backend's driver faults silently fall to the `Unhandled` bucket until someone edits Core again, and the same gap already existed unfixed for Azure (`RequestFailedException` derives straight from `Exception`, so it was never matched) and Kafka. A 1.0 freeze would canonize an inconsistency where one substrate's faults classify and the others don't.

## The decision

- **`IDeadLetterFaultClassifier` is an internal `Edict.Core` seam, first-party only.** `string? Classify(Exception exception)` returns an allow-listed `FailureReasonValues` constant, or `null` to defer to the next classifier. The interface stays `internal`; each contributing provider assembly gets an `InternalsVisibleTo` line (`Edict.Postgres`, `Edict.Kafka`, `Edict.Azure.Persistence`; `Edict.Azure.Streaming` already had one). It is **not** public and **not** in `Edict.Contracts`: it is a provider seam, not consumer wire shape, and keeping it internal preserves the deliberately tiny public surface (`PublicSurfaceAllowListTests`). The cost is conscious: an out-of-tree substrate author cannot register a classifier without an IVT line, and a new in-repo substrate adds a classifier class plus one IVT line — a small, mechanical, non-logic change rather than an edit to the classification switch.

- **Built-ins are privileged and run first; providers only on fallthrough.** Core's framework-type switch runs first and wins. Registered classifiers are consulted only where the result would otherwise be `Unhandled`; the first non-`null` wins, in registration order. A provider classifier therefore cannot hijack a `TimeoutException`, a saga-lifecycle throw, or a serialization fault into `Substrate`. The `Saturated` forward-compat name-match stays in Core (it guards a Core concept, `EdictOutboxSaturatedException`, not a substrate type).

- **Each shipped provider classifies its own driver faults by real type.** `PostgresDeadLetterFaultClassifier` matches `NpgsqlException`/`PostgresException`/`EdictPostgresStorageException`; the Kafka classifier matches `KafkaException`; Azure ships two classifiers — one per independent Azure assembly — both mapping `RequestFailedException → Substrate`. All resolve to existing buckets; none invents a new one. Registration is `TryAddEnumerable(ServiceDescriptor.Singleton<IDeadLetterFaultClassifier, …>())` in each `AddEdict*` extension, so the same impl registered twice is a no-op and multiple providers coexist.

- **The classifier path honours the promoter's no-throw rule.** `Classify` runs inside `DeadLetterPromoter.Promote`, which must never throw (a throw becomes a poison-pill reminder loop). Each registered classifier call is wrapped: a throwing classifier is treated as a defer (`null`) and classification continues to the next, falling to `Unhandled` rather than escaping.

## Considered Options

- **Public interface in `Edict.Contracts` for out-of-tree substrate authors.** Rejected. It would fully deliver the literal "no framework changes" promise for external authors, but Edict's substrates are all first-party and the roadmap list (SQS/Dynamo/NATS/Cosmos/Mongo) is first-party; a public seam grows a guarded surface for an audience that does not yet exist. The README is reworded to the honest claim instead: no **public-API** changes; provider-specific fault classification is a registered extension point.

- **Make built-ins just another registered classifier.** Rejected. Uniform, but it loses the compile-time guarantee that framework arms always win and complicates the no-classifiers-registered default path. Privileged built-ins are the safety property.

- **Return an `EdictDeadLetterFailureReason` enum instead of `string?`.** Rejected. Type-system-enforced bounding is attractive, but it refactors the existing `SemanticConventions` string consts into a parallel representation for marginal benefit given the seam is first-party and reviewed; per-provider tests assert each classifier returns an allow-listed value.

- **Migrate Postgres only, leave Azure/Kafka at `Unhandled`.** Rejected. It fixes the named debt but leaves the very inconsistency the freeze would canonize: one substrate classifies its faults, the others silently do not.

## Consequences

- A reflection presence guard in `Edict.Architecture.Tests` enumerates the four provider assemblies and asserts each ships an `IDeadLetterFaultClassifier`; the enumerated list doubles as the registry of "substrates that must classify", so adding a fifth substrate to it is part of the substrate-add checklist. Each provider also pins its classifier with a direct unit test against the real driver exception type; the Core test pins built-ins-win, fallthrough-consults-providers, and throwing-classifier-degrades-to-`Unhandled`. Core's three Postgres synthetic-type tests are deleted.

- The bucket allow-list is unchanged (`telemetry.md` and ADR-0039 stand): this decision moves *who* assigns `Substrate`, not the set of values. No wire-format or persisted-state change; no Verify snapshot regeneration.

- `Edict.Postgres.Tests/Resilience/README.md` §"Classifier fix" is rewritten from the string-match description to the registered-classifier model.

This ADR records the decision and rationale. ADR-0018 owns the dead-letter forensic surface (and its implementation's former `IsPostgresDriverFault` string-match is superseded here), ADR-0039 the metrics cardinality policy the closed bucket set serves, and ADR-0041 the exception policy and the promoter's no-throw safety net this path must not violate.
