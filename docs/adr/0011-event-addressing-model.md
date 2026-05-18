# Event addressing: domain streams, free re-keying, command-consistent wire mechanism

**Status:** accepted — refines the single-correlation-id prose in ADR 0004 and the `(eventTypeName, sourceAggregateGuid)` framing that prose implied

Events are introduced with three deliberate choices, made for consistency with the existing command spine and for fan-out across a system with hundreds of handlers:

1. **Same wire mechanism as commands.** A concrete event is a `record` deriving from base `Event`, carrying hand-written `[MessagePackObject(keyAsPropertyName: true)]` exactly like `Command` (ADR 0007); the generator auto-emits only the smart `[Alias]` partial exactly like commands (ADR 0010). No `[GenerateSerializer]`/`[Immutable]` — those would reintroduce the generator-ordering trap on the `Event` base in `Edict.Contracts`, and a second serialization stack means a second drift-guard and a second mental model. One mechanism across the framework, even at the cost of consumer-hand-written attributes.

2. **Domain streams, not per-event-type streams.** A stream is domain-scoped (`Sales`, `Orders`) and carries every event type in that domain, named once by a required `[Stream("Name")]` attribute on the concrete event — the single token both the publisher (`Raise` flush target) and every subscriber (generator-emitted `[ImplicitStreamSubscription]`) derive from. There is no namespace default (an EDICT analyzer errors if `[Stream]` is absent). A subscriber to a domain stream is activated for every event type on it and acts only on the types it has a `Handle` overload for; unhandled types are a no-op that consumes no dedup-ring slot.

3. **The event's `[RouteKey]` re-keys freely.** `[RouteKey]` is one framework concept on both messages: on a command it selects the aggregate grain, on an event it selects the stream key the subscriber is activated with. The event key *usually* equals the command key (in-domain case) but **may differ** (cross-domain: a `Sales` handler raising an `Orders`-stream event). The continuous `command → event → handler` correlation is provided by **trace context** (ADR 0003), not by a guaranteed-equal Guid.

## Considered Options

- **Per-event-type streams (namespace = the event's `[Alias]`)** — rejected: with implicit subscriptions keyed by namespace, every consumer wakes for every event type and filters in-process; muddies per-aggregate activation; does not scale to hundreds of handlers across domains.
- **`[GenerateSerializer]`/`[Immutable]` on events** — rejected: hand-written on concrete events they would work, but the `Event` base in the Orleans-free `Edict.Contracts` cannot carry generated Orleans attributes (ADR 0006 trap), and a split MessagePack/Orleans-native serialization story doubles the drift-guard and mental model for no benefit on a transient spine.
- **Stream name on the publishing grain, or on the subscriber only** — rejected: the publisher and subscriber would name the stream independently and could silently disagree. Only the shared event type (contracts assembly, ADR 0008) is a token both sides mechanically derive from.
- **Namespace-leaf default for `[Stream]`** — rejected: a missing attribute silently splitting events across unintended streams is worse than a compile error; the analyzer makes it explicit.
- **Keep the single-correlation-id Guid invariant of ADR 0004** — rejected: the cross-domain `Sales → Order` case is a first-class requirement; the Guid was downgraded from invariant to common case, with trace context as the reliable stitch.

## Consequences

- ADR 0004's "one correlation id flowing command → grain → event → handler" is downgraded to the common case; the term `sourceAggregateGuid` is retired in favour of "the event's route key". Observability (ADR 0003) is unaffected — trace context still stitches the chain regardless of keys.
- New analyzers: event must be `partial` (mirrors EDICT006); event must declare `[Stream]`; projection `Handle` must return `Task` with an `Event`-derived parameter. The existing `RouteKeyAnalyzer` and `GrainMustBePartialAnalyzer` are *extended* to also target events/projection grains rather than duplicated — `[RouteKey]` is one concept, one diagnostic.
- `EventDeduplicationGrain` → `ProjectionBuilderGrain` are built now; `EventHandlerGrain`/`SagaGrain` are deferred but the base is shaped to accept them without rework.
- A new global read model still cannot be back-filled (ADR 0001, no replay); cross-domain re-keying widens an existing forward-only hazard, it does not add a new one.
