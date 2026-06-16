# Edict

Edict is a CQRS, event-driven framework built on Microsoft Orleans. It provides reusable grain-based building blocks — command handlers, event handlers, sagas, projection builders — with implicit stream subscriptions, OpenTelemetry observability, and idempotent event delivery. Edict is **event-driven, not event-sourced**: there is no event store, no replay, and no rebuild from history.

## Naming convention (brand)

Edict is treated as a brand. A type carries the **`Edict` prefix** if and only if **(a)** a consumer, or its generator-emitted code, types it — derives from it, applies it as an attribute, or receives/returns it — **or (b)** it is an inheritance root shared by the consumer-facing bases (grain or DI-service). Internal infrastructure that satisfies neither stays unprefixed and descriptively named. Consumer subclasses are named `{Name}{Role}` (`OrderCommandHandler`, `OrdersByStatusProjectionBuilder`, `OrderPlaceCommandValidator`).
_Avoid_: bare `Command`/`Event` base names; the `Grain` suffix on any Edict abstraction or consumer subclass; prefixing internal types the consumer never references. Raw Orleans test doubles that genuinely derive from `Grain` keep "Grain" — they are not Edict abstractions.

## Language

**EdictCommand**:
An expression of intent to change state, addressed to exactly one grain via a direct grain call and handled by a Command Handler.
_Avoid_: trace fields on `Command`; past-tense command names.

**Event**:
A notification that something happened, broadcast on a domain stream to zero or more subscribers and discarded after handling.
_Avoid_: assuming the event key equals the command key; treating the per-message `[RouteKey]` Guid as a chain identifier (it re-keys across domains; the framework-stamped Correlation Id is the chain-stable token that rides every hop, with W3C trace context as its observability twin).

**Telemeterized**:
An attribute placed on a primitive property of a `Command`/`Event` subclass that causes the generator to emit code writing the property as an OpenTelemetry tag of the form `edict.{snake_case_property_name}` on the active span — for a Command, the `edict.command` span; for an Event, both the `edict.event.publish` and `edict.event.handle` spans. The tag key is shared across declaring types so the same domain concept queries by a single key.
_Avoid_: runtime reflection; auto-tagging properties that are not annotated; type-prefixing the tag key; tagging the `edict.event.deduplicated` span (forensic-only, no query story).

**Trace context**:
The persisted W3C context (`TraceId`/`SpanId`/`TraceState`) stamped on a message or durable entry — on `EdictEvent`, on `OutboxEntry.TraceParent`, and on the arm-context fields of a `ScheduleEntry` and saga lifecycle state — captured by the turn that published or armed and read by the turn that consumes or fires. A trace is one grain's single synchronous turn; this context is the **link carrier** the consuming turn rebuilds an `ActivityLink` from, so the new turn's trace links back to its cause rather than nesting under it.
_Avoid_: reading it as a parent pointer (the cross-turn hop is a link, not parent-child — only the awaited API `edict.command` → `edict.command.handle` stays parent-child); conflating it with the Correlation Id (the chain-stable Guid that survives an unsampled trace, where this context is null); forcing the recorded flag on restore (the real sampled flag rides the context, so a dropped turn stays dropped).

**Command Handler**:
The Guid-keyed aggregate grain that accepts Commands, performs the state change, and may raise Events, with framework-owned durable aggregate state committed whenever a handler completes — independent of whether it raised an Event.
_Avoid_: holding aggregate state in plain grain fields; a non-generic stateless `EdictCommandHandler` as the consumer base; assuming `State` persists only when an Event is raised (it persists on every completing `HandleAsync`, including one that returns `Rejected`); mutating `State` on a path that throws (the partial mutation is discarded and the activation dropped).

**RouteKey**:
The `[RouteKey]` attribute marking the single `Guid` property that addresses a message — on a Command it selects the aggregate grain, on an Event it selects the stream key.
_Avoid_: `[Key]` (collides with `System.ComponentModel.DataAnnotations`); non-Guid keys; more than one per message; assuming the event key equals the command key.

**Command Result**:
The outcome envelope a Command Handler returns: `Accepted` or `Rejected` (with reasons), carrying no domain data; it is the caller's answer only and does not gate persistence — a completing handler's `State` mutations and raised Events commit on both outcomes. `Accepted` also carries an `EdictCursor` for read-your-writes.
_Avoid_: returning domain payloads through a command; throwing for expected rejection; treating `Rejected` as a rollback (it is not — what the handler did is still committed, only a throw discards).

**Correlation Id**:
A framework-stamped Guid that rides every Command and Event on one causal chain. It is minted if absent when a Command is first sent and inherited by every message that chain causes — a Command's raised Events, a Saga's dispatched Command — so a timer or schedule fire, having no upstream message, starts a fresh chain. It is the chain-stable identifier behind read-your-writes and an optional grouping dimension on dead-letter rows.
_Avoid_: authoring it by hand (the framework stamps it; a caller may supply one but need not); conflating it with the per-message `[RouteKey]` Guid (which re-keys across domains) or with W3C trace context (the observability twin, null when unsampled); expecting a per-hop causation parent (it is constant across the chain, not a parent pointer).

**Principal** (`EdictPrincipal`):
The actor on whose authority a Command was issued or an Event raised: a human user, a service identity, or a consumer-minted system identity for non-user work. An opaque consumer-supplied string, resolved at the edge from the authenticated claim, stamped on the message at origin and carried unchanged through the consequential Command/Event/schedule chain. A durable field on `EdictCommand` and `EdictEvent` (mirroring the Correlation Id), with `RequestContext` only as the per-turn relay.
_Avoid_: trusting a principal read from a Command body (confused-deputy); stamping a constant or `system` principal on a user-initiated send (misattribution — it records a real person's decision under a fabricated actor; a `system` principal is correct only for an actor-less origin); conflating it with the **Data Subject** (the person the data is *about*); Edict-side validation of its format (the consumer's domain); a framework-supplied "system" sentinel (the consumer mints their own for actor-less origins).

**Data Subject**:
The person an audited record is *about*, in the GDPR sense, as distinct from the **Principal**, who is the actor that acted: an admin (the principal) editing a customer's address makes the customer the data subject, so the two routinely differ. Edict does not model the data subject today: the audit log captures the principal, and subject-keyed concerns such as erasure are deferred.
_Avoid_: reading the principal field as the data subject (it names who acted, not who the data is about); assuming Edict can answer a subject-keyed query today (it captures the principal only).

**Origin send**:
A Command send a consumer writes at the edge of a causal chain — `IEdictSender.SendAsync(command)` — where the **Principal** is stamped for the first time (from the edge resolver, or explicitly via the `SendAsync(command, principal)` overload). The fail-closed gate and the opt-in `EDICT023` analyzer apply here and only here.
_Avoid_: treating a **Relayed send** as an origin (it inherits, never re-resolves); calling a raised Event an origin (Events inherit in-turn and are never origin-sent).

**Relayed send**:
A Command send the framework issues on a consumer's behalf as a consequence of a turn already in flight — a Saga's `Dispatch`, the outbox `SendCommand` effect, a schedule fire — which inherits the **Principal** from the per-turn relay or the schedule arm-context rather than re-resolving it. Carries the `IsCrossTurnLink()` marker, so it passes the origin fail-closed gate untouched and is never an `EDICT023` call site (a consumer never writes one).
_Avoid_: flagging or failing-closed on a relayed send; confusing the per-turn `RequestContext` relay (ephemeral) with the durable Principal field that re-seeds it at each grain entry.

**EdictCursor**:
The opaque read-your-writes token echoed on `EdictCommandResult.Accepted`, wrapping the Command's Correlation Id. A consumer feeds it to a Projection Read as `after:` to wait, briefly and boundedly, until the work the Command set in motion is visible.
_Avoid_: unwrapping it to the bare Guid on the read path (pass the cursor); minting one to force a wait on work no Command set in motion; reading a returned cursor as proof the whole chain has landed (it names the chain; the read decides visibility).

**Command Validator**:
A server-side, no-mutation precondition gate for a Command, run within the same activation turn before `HandleAsync`, answering whether the Command is admissible against current aggregate state. Authored as `{Name}CommandValidator : EdictCommandValidator<TCommand>` — an Edict-owned thin base over `FluentValidation.AbstractValidator<TCommand>`. Discovered automatically by `AddEdict()` from the same assemblies it scans for handlers; no manual DI registration.
_Avoid_: mutating state in a validator; client-side validation; throwing for validation failure; expressing transition-time outcomes (only discoverable while mutating) as validator rules; deriving consumer validators directly from `AbstractValidator<T>` (use `EdictCommandValidator<T>` so the brand and MCP discovery work).

**Event Handler**:
A terminal grain that subscribes implicitly to an Event stream and reacts by performing external side effects (sending email, calling an HTTP API, writing to a non-Edict store).
_Avoid_: owning events; calling `Raise`/`Dispatch` from a handler; treating dedup-window commitment as "the side effect happened"; inlining external I/O on the stream callback by rolling your own `EdictIdempotencyBase` subclass.

**Saga**:
A grain that coordinates a multi-step workflow by reacting to Events, holding durable `Progress`, and issuing at most one Command per Event via `Dispatch`. A handler may mutate `Progress` and dispatch nothing (accumulate now, act on a later Event); the mutation still commits, because every handled Event commits its dedup-ring slot and `Progress` rides the same atomic write.
_Avoid_: dispatching more than one command per handled event; reading "at most one" as "exactly one" (zero dispatches is a valid handle, not a dropped one); expecting `Dispatch` to buffer like `Raise`; reconstructing progress by replay.

**Saga Timeout**:
A saga's absolute lifetime cap: a deadline armed once when the saga handles its first Event and never reset by later activity, declared with `[EdictSagaTimeout]` (a duration literal or `Unbounded`) and otherwise inheriting the silo-wide default (ships finite at 7 days).
_Avoid_: reading it as a per-step or idle deadline; writing `"24:00:00"` for one day (the leading field is days, so that is 24 days; use `"1.00:00:00"`).

**Complete**:
The hard-terminal signal a saga raises from a handler (`Complete()`) to mark its workflow successfully finished: the lifecycle moves to `Completed` in the same atomic write, the cap reminder is unregistered, and any later genuinely-new Event the saga handles dead-letters (unrelated event types off the shared stream are still ignored).
_Avoid_: calling it on a saga whose key may legitimately receive a later Event (leave a re-openable coordinator live and rely on the cap); expecting it to mutate `Progress`.

**Compensation**:
The single Command a saga issues to undo or unwind partial work, either from a normal handler on a failure Event or from the `OnSagaTimeoutAsync()` hook when the cap fires; an un-overridden fired cap dead-letters instead.
_Avoid_: dispatching more than one compensating Command; treating dead-letter as compensation.

**EdictSchedule**:
A Command Handler's first-class way to run recurring work on its own clock, started from inside `HandleAsync` with `Schedule(message, every:, timeout:)`. The schedule persists a serializable `EdictScheduleMessage` (data plus its `[Alias]`), never a delegate; each fire deserializes it, routes it back through the handler's generated dispatch, and re-enters the full handler lifecycle, so a fire gets throw-rollback, atomic state-plus-outbox commit, and trace nesting like any other handler. The fire handler returns only `Continue` or `Complete` (`EdictScheduleResult`); cadence is declared once at the call site. A timeout cap (armed once, never reset by ticks) inherits a finite silo default (`EdictCommandHandlerScheduleOptions.DefaultTimeout`, 7 days) unless given an explicit `timeout:` or opted out with `EdictSchedule.Unbounded`; on cap it runs `OnScheduleTimeoutAsync(message)` if written, else dead-letters. Durable across deactivation (a grain timer for sub-minute precision, the `edict-schedule` Reminder as the one-minute backstop); missed ticks coalesce to a single catch-up fire. Replaces the raw `RegisterGrainTimer` + `CommitAndDrainRaisedEventsAsync` escape hatch.
_Avoid_: persisting a delegate or closure (only the message is durable; read everything else from `State`); declaring the cadence anywhere but `Schedule(...)`; expecting more than `Continue`/`Complete` from a fire; confusing the (per-schedule, call-argument) **schedule timeout** with the **Saga Timeout** (per-saga, a class attribute) — they share vocabulary by design but are scoped to different hosts.

**Projection Builder**:
A grain that consumes the live Event stream and maintains a current-state read model, processing the stream only forward. Two species share the abstract root `EdictProjectionBuilderBase<TPayload>` (which owns the read-your-writes cursor mechanism): a **State Projection Builder** keeps the read model in grain state, a **List Projection Builder** keeps it in an external store. The consumer picks per read model: in-grain for small, hot, per-aggregate state (cheaper inline commit, but it inflates activation), external for large or unbounded read models.
_Avoid_: implying replay, rehydration, or "rebuild the projection"; reading "Projection Builder" as a base a consumer derives directly (the species bases are `EdictProjectionBuilder<TProjection>` and `EdictListProjectionBuilder<TListProjection>`; the bare `EdictProjectionBuilder` is the in-grain species, not the root).

**Sender**:
The DI-injected `IEdictSender` with a single `Task<CommandResult> SendAsync(Command)` that resolves the owning aggregate by `[RouteKey]` and dispatches.
_Avoid_: static/extension-method send (bypasses DI, defeats the in-memory test swap); per-command overloads.

**Domain Stream**:
A named Orleans stream that carries every event type for one domain, declared once via `[Stream("Name")]` on the concrete event type.
_Avoid_: per-event-type streams; inferring the stream name from the CLR namespace; a publisher and subscriber naming the stream independently.

**List Projection Builder**:
A Projection Builder whose read model lives in an external composite-key store instead of grain state, so grain activation stays small no matter how large the read model grows. The grain holds a transient last-touched-slot cache of the row in memory so consecutive events on the same `(pk, rk)` skip the store read; the *durable* read model still lives in the external store. The base type (`EdictListProjectionBuilder<TListProjection>`), not a storage word in the class name, marks the species, so consumer subclasses are named `{Name}ProjectionBuilder`. Read through `IEdictListProjectionReader<TListProjection>`.
_Avoid_: reading the store directly instead of via the Projection Reader; putting the *durable* read model in grain state "to be safe" (that is the State Projection Builder's job, a deliberate choice, not a safety hedge); treating the external store as Azure-specific; putting an `ITableEntity`/storage type on the row; carrying a storage word ("Table") in the subclass name.

**State Projection Builder**:
A Projection Builder whose read model lives in the grain's own durable state (the `Payload` slot of the persisted envelope), committed inline with the dedup ring in one write: no external store, no outbox effect, so read-your-writes resolves the instant the write lands rather than after an asynchronous drain. Authored as `{Name}ProjectionBuilder : EdictProjectionBuilder<TProjection>`, the read model exposed through a `Projection` accessor the handler mutates, and read through `IEdictProjectionReader<TProjection>.ReadAsync(key)`. Structurally a saga without `Dispatch`: one grain holds one projection object.
_Avoid_: using it for large or unbounded read models (grain state inflates activation latency, the consumer's call to make); putting a storage word in the subclass name; expecting a row/partition read (the grain is the single read model, so `ReadAsync` returns the whole object).

**Projection Reader**:
The framework-provided read-only, storage-neutral seam the application uses to read a Projection Builder's output, mirroring `IEdictSender` on the command side. One interface per species: `IEdictProjectionReader<TProjection>` (`ReadAsync(key)`, returns the whole in-grain projection) for a State Projection Builder, and `IEdictListProjectionReader<TListProjection>` (`GetAsync`/`QueryPartitionAsync`) for a List Projection Builder. Reads route through the projection grain (not the backing store directly), so the read API carries no storage detail and the activation that owns the read model is on the read path.
_Avoid_: reaching for the framework-internal write/store seam from application code; expecting a write method (the reader is read-only); injecting the wrong species' reader for a projection type (it resolves the grain but the mismatched read throws `EdictUnsupportedProjectionReadException` at runtime, not compile time).

**Projection Read**:
The typed tri-state a Projection Reader returns (`EdictProjectionRead<TRow>` for a point-get, `EdictProjectionPartitionRead<TRow>` for a partition query): the row or rows plus an `EdictReadStatus` of `Immediate` (no cursor, the poll path), `CursorReached` (the cursor's correlation is visible), or `CursorTimedOut` (the bounded wait elapsed; the latest available row is still returned). With no cursor a read answers immediately; with an `EdictCursor` it waits until the named correlation's first effect on this projection is visible, falling back to the bounded `EdictOptions.ProjectionReadTimeout` unless an explicit infinite timeout is passed. Eventual-consistency lag is an expected outcome carried in the status, never a throw; only caller cancellation throws.
_Avoid_: reading lag as a fault; assuming `CursorReached` means every effect of the correlation has landed (it is any-applied: at least the first effect is visible, so prefer one Event per Command where exact read-your-writes matters); passing an explicit infinite timeout by accident (an omitted timeout is bounded, never infinite).

**Outbox**:
The single durable-delivery engine, owned by both grain roots, that records pending effects (`PublishEvent`, `SendCommand`, `UpsertRow`, `InvokeHandler`) in the same grain-state write as the consumer payload. A `PublishEvent` entry's `EventId` is stamped once as the event is enqueued and persisted on the payload, so a re-publish reuses the committed id rather than minting a new one.
_Avoid_: an Outbox grain; a second store for entries; assuming exactly-once publish (it is at-least-once — consumer dedup makes it effectively-once); assuming per-aggregate causal order across multiple events once any entry has failed.

**Dead Letter**:
The terminal, forensic-only tail of the Outbox: a permanently failing effect is recorded into a fleet-wide dead-letter projection without blocking aggregate intake.
_Avoid_: an in-grain dead-letter slice or cap; blocking aggregate intake when downstreams fail; expecting a redrive affordance; treating dead-lettering as a recovery mechanism rather than an RCA surface; reading the dead-letter table directly instead of via `IEdictDeadLetterRepository`.

**Fault Classification**:
Mapping a dead-lettered failure to one of a closed allow-list of failure-reason buckets (an RCA dimension on the dead-letter metrics): framework causes are classified in `Edict.Core`, while each persistence/streaming provider recognises its own driver faults through a registered `IDeadLetterFaultClassifier` consulted only when no framework cause matched.
_Avoid_: inventing a new bucket value (the set is closed for metric cardinality); name-matching a provider's exception types inside `Edict.Core`; letting a provider classifier override a framework cause or throw on the promoter's no-throw path.

**Audit Record** (`EdictAuditRecord`):
An immutable, attributable statement that a decision happened: a Command's `Accepted`/`Rejected` outcome (captured at C1, `EdictCommandHandler.ValidateAndHandleAsync`) or an Event's occurrence (captured at E1, the outbox enqueue point), committed atomically in the decision's own grain-state write and drained to a tamper-evident store. Holds attribution (Principal), causal spine (Correlation Id, record id), message type, outcome, and a payload hash plus a reference into a separate payload store — never the body inline. Read back through `IEdictAuditRepository`.
_Avoid_: conflating it with the dead-letter row (a separate forensic sibling, not a unified type with an outcome discriminator); inlining the payload into the chain (it must stay separately addressable); reading it as event sourcing (no replay, no rebuild); capturing it from a stream subscriber (that misses Commands and rejections).

**Audit Chain**:
The per-aggregate hash chain that makes one Command Handler aggregate's audit history tamper-evident: `prev_hash` lives in the grain's state, `this_hash` is computed over the stored record bytes, and global cross-aggregate order is reconstructed at query time from the Correlation Id and `OccurredAt`. Verified through `IEdictAuditRepository.VerifyEntityChainAsync`.
_Avoid_: a single global chain (a bottleneck sequencer grain Orleans warns against); recomputing the hash from a fresh re-serialization rather than the stored bytes (a MessagePack version bump would break verification); expecting tamper-*prevention* from it (it is tamper-*evidence*; infrastructure WORM is the prevention layer).

**Audit Payload**:
The captured body of an audited Command or Event, held in a separate append-only store (`IEdictAuditPayloadStore`) keyed by the audit record id, and read through `IEdictAuditRepository` either as raw bytes (`GetPayloadAsync`, the integrity anchor the `PayloadHash` was computed over) or deserialized back to the typed message the consumer authored (`GetMessageAsync`). Distinct from the **Claim Check**, which is event-only, `EventId`-keyed, on the delivery lifecycle, and reapable: the Audit Payload holds every audited Command *and* Event body and is infinite-retention alongside the rest of the audit log.
_Avoid_: conflating its store with the claim-check store (a separate seam on a separate lifecycle); deleting, renaming, or breaking-changing an audited message type — or its `[Alias]` — once auditing is on (that silently severs the typed `GetMessageAsync` read of every body already captured under that type; the bytes and hash survive, the typed read does not — audited message types are append-only); reading the typed message as proof of integrity (the bytes plus `PayloadHash` are the anchor, the typed read a convenience over them).

**Event Envelope** (`EdictEventEnvelope`):
The universal wire-format wrapper carried on every Edict stream hop, holding either an inline payload or a Claim Check pointer and unwrapped before dispatch.
_Avoid_: deriving consumer event types from `EdictEventEnvelope`; reading it on a consumer `HandleAsync` signature; treating it as solely a claim-check vehicle.

**Claim Check**:
The escape hatch for oversized events: the body is written to an append-only store keyed by the event's `EventId`, and the wire hop carries no separate pointer — the envelope's `EventId` **is** the key. The receiver fetches by `EventId`. `PutAsync` runs exactly once at the outbox enqueue boundary with a freshly-minted unique `EventId` and is never re-called on re-drain, so a duplicate-key write is a loud collision-detector (Postgres PK conflict, Azure 409 on `overwrite:false`), not an idempotent rewrite.
_Avoid_: minting a separate claim-check key (the EventId is the key — the store does not generate or return one); deleting blobs from framework code (append-only is load-bearing); estimating event size by anything other than the serialised byte length; fetching the body into the dead-letter row; treating blobs as an event log.

**Idempotency Base** (`EdictIdempotencyBase<TPayload>`):
The abstract generic base that Event Handlers, Sagas, and Projection Builders inherit, providing a bounded per-grain window of recently handled `EventId`s that suppresses at-least-once redeliveries. The dedup key is the `EventId` assigned once as the event enters the Outbox, so it stays constant across redeliveries and producer-side re-publishes (a crash-recovery re-drain ships the same id, not a fresh one).
_Avoid_: implying it owns or configures stream subscription.

**Substrate**:
The backend pairing — one streaming provider plus one persistence provider — an Edict silo runs on; the two reference pairings are Azure (`Azure.Streaming` + `Azure.Persistence`) and Kafka+Postgres. The `Edict.Substrate` library and its `Edict.Substrate.Azurite` / `Edict.Substrate.KafkaPostgres` implementations are a separate concept: harness infrastructure (ADR-0030) that the benchmark fixtures use to bring a backend up and tear it down — not a production runtime concept. Conformance no longer uses `Edict.Substrate`: the axis batteries host their backends directly (ADR-0054).
_Avoid_: calling `Edict.Substrate.*` libraries a "production substrate" (they are harness implementations); treating "substrate" unqualified in code when streaming-vs-persistence is what matters.

## Relationships

- A **Command Validator** gates a **Command** in the same activation turn before its **Command Handler**'s `HandleAsync` runs, reads but never mutates state, and yields a `Rejected` **Command Result** on failure
- A **Command Handler** handles **Commands**, mutates durable `State`, returns a **Command Result**, and may raise **Events**
- A **Saga** reacts to an **Event**, mutates durable `Progress`, and issues at most one **Command** (zero or one); a dispatch-nothing handle still persists its `Progress`
- The **Outbox** is one engine with four effect kinds; a permanently failing entry is **dead-lettered**; delivery is at-least-once and made effectively-once by the **Idempotency Base**
- A **Command** is routed to exactly one aggregate grain by its single `[RouteKey]` Guid; a creation command routes identically — the caller mints the Guid and Orleans' virtual grains make the not-yet-activated aggregate addressable
- A consumer issues a **Command** through the **Sender**; the **Sender** is the seam `Edict.Testing` swaps for an in-memory implementation
- **Event Handlers**, **Sagas**, and **Projection Builders** subscribe to **Events** via implicit stream subscriptions and all inherit the **Idempotency Base**
- An **Event** is published to its **Domain Stream** (named by `[Stream]` on the event), keyed by the event's `[RouteKey]` Guid; every subscriber to that stream is activated with that Guid and acts only on event types it has a `HandleAsync` overload for. A fixed-Guid singleton is the explicit escape hatch for a global read model.

## Example dialogue

> **Consumer:** "My `ProjectionBuilder` for `OrderPlaced` — how does it get the historical orders when it first starts?"
> **Edict author:** "It doesn't. Edict is event-driven, not event-sourced — there's no replay. A Projection Builder only ever sees events from the moment it's subscribed, forward."
> **Consumer:** "And if the same `OrderPlaced` is delivered to it twice?"
> **Edict author:** "It inherits `EdictIdempotencyBase`. The `EventId` dedup window suppresses the second delivery for *that* projection — but the same event still reaches your `OrderEmailHandler`, because dedup is per consuming grain, not global."
>
> **Consumer:** "Where does 'can't cancel an already-shipped order' go — a Command Validator or `HandleAsync`?"
> **Edict author:** "Either could read the state, but the line is mutation. The validator is a precondition gate: it inspects current state and rejects *before* any transition, no writes. `HandleAsync` owns the transition. If the rule is knowable from current state without attempting the change, it's a validator; if the rejection only emerges *while* mutating, it's a `HandleAsync` outcome. Both return the same `Rejected` — they differ by *when* and *whether they mutate*, not by the envelope."

## Flagged ambiguities

_None currently._
