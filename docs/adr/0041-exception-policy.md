# Exception policy (Edict-typed throws, wiring-vs-runtime split, promoter never throws)

All production framework throws are `Edict*`-typed. The dead-letter row's `ExceptionType` column, the OpenTelemetry exception event, and any consumer `catch` all key on the type, so untyped throws lose cause information at every observability surface.

The remedy splits by lifecycle. A **wiring fault** — knowable before the silo runs (missing client, missing options, missing provider marker, duplicate registrar) — throws `EdictWiringException` from the extension call site or from `EdictWiringValidator` at host start. Never from a `HandleAsync` call. A **runtime fault** throws a per-cause-narrative `Edict*` exception (`EdictUnregisteredTypeException`, `EdictClaimCheckFetchException`, `EdictSagaCoordinationException`, `EdictInternalInvariantException`, etc.), is caught by `OutboxHost.ExecuteGroupCapturingAsync`, classified by `DeadLetterFailureClassifier`, and dead-lettered per ADR 0018.

Nothing reached from `DeadLetterPromoter.Promote()` may throw — it is the safety net, no catch below. It logs, emits `PromotionFailureCount`, and returns a synthetic dead-letter row with the would-be exception's type as a string marker.

## Considered Options

- **Status quo — system `InvalidOperationException` / `NotSupportedException` / `ArgumentException`** — rejected: `DeadLetterFailureClassifier` falls through to `Unhandled`, dead-letter rows lose cause information, operator filtering on `ExceptionType` doesn't work.
- **One umbrella `EdictRuntimeException` with a `Reason` field** — rejected: removes the classifier's ability to discriminate by type, forces every dead-letter consumer to parse a string.
- **Per-throw-site type** — rejected: one-throw-one-type sprawl with no consumer-catch story; `EdictUnregisteredEventException` / `EdictUnregisteredRowAliasException` / `EdictUnregisteredStreamException` collapse into one cause-narrative ("consumer raised something from an unscanned assembly") with a `Kind` discriminator.
- **`EdictWiringException` subtype hierarchy** — rejected: wiring throws fire pre-HandleAsync, never classified by the dead-letter pipeline, so type isn't a bucketing key; the aggregated `EdictWiringValidator` problem list matches a single-type-with-message shape (ADR 0017's empty-handlers warning + `UnroutableCommandException` precedent).
- **Architecture test enforcing "no throws reachable from `Promote()`"** — rejected: convention in CLAUDE.md + reviewer pressure suffices; the Roslyn syntax walk costs more than the catch.
- **Throw inside `DeadLetterPromoter`** — rejected: `Promote()` is called from `OutboxHost.cs:335`, outside the engine's catch at line 400; a throw propagates up the grain drain method, state is not written, the failed entry stays Pending, the next reminder fires the same throw — poison-pill loop.
- **All new types in `Edict.Contracts` for forward-compat** — rejected: producer-catch is the only gate (ADR 0007); no new type has a producer-catch story; placement can move later while there are no released consumers.

## Consequences

- Existing throws migrate touch-as-you-go in three PR-sized slices (wiring-time + validator pull-ups; runtime hot-path types + classifier mapping; `DeadLetterPromoter` no-throw behaviour change).
- `DeadLetterFailureClassifier` gains `Wiring`, `ConsumerBug`, `InternalBug` buckets — checked against ADR 0039's cardinality budget in the implementing PR.
- The wiring-revealed-at-runtime throws (`OutboxHost.cs:187`, `ClaimCheckUnwrap.cs:58`, `ClaimCheckPolicy.cs:74`, `InvokeHandlerExecutor.cs:30`) move to `EdictWiringValidator`. A misconfigured silo fails at host start with one aggregated message instead of crashing every `HandleAsync()` call. Observable to integration tests that previously asserted on the runtime throw.
- `UnroutableCommandException` stays in Core. Its catch story is silo-only despite the type name suggesting consumer relevance — flagged for revisit if a producer-tier setup wrapper ever lands.
- Dead-letter row's `ExceptionType` column moves from `System.InvalidOperationException` (or Orleans / Npgsql wrappers) to stable `Edict*` names. Downstream telemetry filtering on the column rebases — safe with no released consumers.
