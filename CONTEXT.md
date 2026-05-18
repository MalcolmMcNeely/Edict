# Edict

Edict is a CQRS, event-driven framework built on Microsoft Orleans. It provides reusable grain-based building blocks — command handlers, event handlers, sagas, projection builders — with implicit stream subscriptions, OpenTelemetry observability, and idempotent event delivery. Edict is **event-driven, not event-sourced**: there is no event store, no replay, and no rebuild from history.

## Naming convention (brand)

Edict is treated as a brand. A type carries the **`Edict` prefix** if and only if a *consumer* types it — derives from it, applies it as an attribute, or receives/returns it. Internal infrastructure a consumer never names stays unprefixed and descriptively named. This supersedes the earlier "no prefix" decision (the `Command`/`Event` bare names). Branded surface includes `EdictCommand`, `EdictEvent`, `EdictCommandResult`, `EdictRejectionReason`, `[EdictRouteKey]`, `[EdictStream]`, `[EdictTelemeterized]`, the grain bases (`EdictCommandHandlerGrain`, `EdictEventDeduplicationGrain`, `EdictProjectionBuilderGrain`, `EdictTableProjectionBuilderGrain`), `IEdictSender`, `IEdictTableRepository`. Internals (the sender implementation, command-route resolver, dedup state) stay bare.
_Avoid_: bare `Command`/`Event` base names; prefixing internal types the consumer never references (the prefix must keep signalling "this is your contract with the framework").

## Language

**EdictCommand**:
An expression of intent to change state, addressed to exactly one grain via a **direct grain call** (not streamed). Handled by a Command Handler. Concrete commands derive from the abstract base class `EdictCommand` (carries `CommandId` only). A command does **not** carry trace-correlation fields: a direct grain call propagates `Activity` context natively, so trace fields live solely on `Event` (ADR 0003). Imperative naming (`PlaceOrder`, `UpdateOrder`) — past-tense names are Events.
_Avoid_: trace fields on `Command`, past-tense command names.

**Event**:
A notification that something happened, broadcast on a domain stream to zero or more subscribers. Transient — delivered, handled, then discarded. Never persisted to a replayable log. Concrete events derive from the abstract base class `Event` (carries `EventId`, `TraceId`, `SpanId`, `TraceState`, `OccurredAt`). A concrete event marks exactly one `Guid` property with `[RouteKey]` (the same attribute commands use); that Guid is the stream key the subscriber is activated with. An event's `[RouteKey]` is set by the consumer inside `Handle` and **may differ** from the command's `[RouteKey]` — re-keying is a first-class case (e.g. a `Sales` command raising an event consumed in the `Orders` domain).
_Avoid_: assuming the event key equals the command key; treating the command→event Guid as a guaranteed-continuous correlation id (it is the common case, not an invariant — trace context, not the Guid, is what reliably stitches the chain).

**Telemeterized**:
An attribute placed on a primitive property of a `Command`/`Event` subclass. A source generator emits code that writes the property as an OpenTelemetry tag (`edict.{type}.{property}`) on the active span. Placing it on a non-primitive is a **compile error**.
_Avoid_: runtime reflection, auto-tagging properties that are not annotated.

**Command Handler**:
The aggregate grain — Guid-keyed — that accepts Commands, performs the state change, and may raise Events. One grain *type per aggregate* handles *many* command types; the consumer writes one strongly-typed `Handle(TCommand)` method per command on a `partial` grain class and never authors the Orleans interface (the source generator emits it, the dispatch, telemetry, and the sender).

**RouteKey**:
The `[RouteKey]` attribute marking the single `Guid` property that addresses a message. One framework concept, two transports: on a **Command** it selects the aggregate grain (the grain key); on an **Event** it selects the stream key the subscriber is activated with. Exactly one per message; must be `Guid` (analyzer-enforced). The command key and the event key it raises *often* coincide (the in-domain case) but are **independent addressing concerns** — the event may carry a different Guid (the cross-domain re-keying case). The continuous command→event correlation is provided by trace context (ADR 0003), not by a guaranteed-equal Guid.
_Avoid_: `[Key]` (collides with `System.ComponentModel.DataAnnotations`), non-Guid keys, more than one per message, assuming the event key equals the command key.

**Command Result**:
The outcome envelope a Command Handler returns: `Accepted` or `Rejected` (with reasons). Rejection is a first-class *outcome*, never an exception (exceptions across the grain boundary are reserved for *infrastructure* faults — timeout, dead grain). Produced in two places that share this one envelope: a **Command Validator** (precondition failure) or `Handle` (transition-time outcome). Carries no domain data.
_Avoid_: returning domain payloads through a command; throwing for expected rejection.

**Command Validator**:
A server-side **precondition gate** for a Command, run inside the generated `Dispatch` *before* `Handle` and within the **same Orleans activation turn**, so the state it inspects cannot be raced before `Handle` acts. It may read the aggregate's current state but performs **no mutation**; it answers "is this Command admissible against the aggregate as it stands?" Authored by the consumer as a FluentValidation `AbstractValidator<TCommand>` (opt-in per command); current state is supplied via the validation context, never via a validator field. Failures map to `RejectionReason`s and yield a `Rejected` **Command Result**.
_Avoid_: mutating state in a validator; client-side validation; throwing for validation failure; expressing transition-time outcomes (only discoverable while mutating) as validator rules.

**Event Handler**:
A grain that subscribes (implicitly) to an Event stream and reacts to Events. Does not own the events.

**Saga**:
A grain that coordinates a multi-step workflow by reacting to Events and issuing Commands. Holds its own durable progress state; does not replay events to reconstruct it.

**Projection Builder**:
A grain that consumes the live Event stream and maintains a current-state read model. The word "projection" is borrowed from event sourcing, but there is **no rebuild and no replay** — it only ever processes the live stream forward.
_Avoid_: implying replay, rehydration, or "rebuild the projection".

**Sender**:
The DI-injected `IEdictSender` with a single `Task<CommandResult> Send(Command)`. The source generator backs it: it reads the command's `[RouteKey]`, resolves the one owning aggregate grain, and dispatches. It is the substitution seam — `Edict.Testing` registers an in-memory implementation so consumer code is identical under test and in production.
_Avoid_: static/extension-method send (bypasses DI, defeats the in-memory test swap), per-command overloads.

**Domain Stream**:
A named Orleans stream that carries *all* event types for one domain (e.g. `Sales`, `Orders`) — streams are domain-scoped, not event-type-scoped. The name is declared once via a `[Stream("Name")]` attribute on the concrete event type — the single token both the publisher (`Raise` flush target) and every subscriber (generator-emitted `[ImplicitStreamSubscription]`) derive from. There is **no default**: omitting `[Stream]` is an analyzer error. An event may belong to a stream outside its producer's domain (cross-domain: a `Sales` handler raising an `Orders`-stream event). A subscriber to a domain stream is activated for *every* event type on it and only acts on the types it has a `Handle` overload for.
_Avoid_: per-event-type streams; inferring the stream name from the CLR namespace; a publisher and subscriber naming the stream independently.

**Table Projection Builder**:
A **Projection Builder** whose read model lives in an external composite-key store instead of grain state, so grain activation stays small no matter how large the read model grows. The mechanism is **persistence-agnostic** — Azure Table Storage is one implementation (shipped in `Edict.Azure`); a different backing store (e.g. a future `Edict.DynamoDB`) is another. The grain base `EdictTableProjectionBuilderGrain` lives in `Edict.Core` and depends only on a framework-internal write store seam (a "dumb" `Get(pk,rk)` / `Upsert(pk,rk,row)`); the grain itself owns the per-event load→apply→writeback orchestration so it is identical across providers. The row is a plain POCO (`T : class, new()`) — it carries no storage type and no keys; PartitionKey/RowKey travel through the seam as parameters (the grain computes pk from its primary key, rk via `GetRowKey`). The application reads the result only through the read-only **Table Repository** — never the grain. The dedup ring stays in persisted grain state, so the row write and the ring commit are **two non-atomic stores**: a crash between them double-applies on redelivery. This gap is **knowingly accepted for now** and closed later by the **Outbox**.
_Avoid_: assuming exactly-once table writes before the Outbox exists; reading the store directly instead of via the Table Repository; putting the read model in grain state "to be safe"; treating "Table" as Azure-specific (it is the generic composite-key-store mechanism) or putting an `ITableEntity`/storage type on the row.

**Table Repository**:
The framework-provided **read-only**, persistence-neutral interface (`IEdictTableRepository`, in `Edict.Contracts`) the application uses to read a **Table Projection Builder**'s output: point-get by (PartitionKey, RowKey) and a partition-scoped query. It is a distinct seam from the framework-internal *write* store the grain uses — the application never writes, and never depends on the write seam. The Azure implementation lives in `Edict.Azure`.

**Outbox** _(planned, not yet built)_:
The future mechanism that will make a **Table Projection Builder**'s row write and dedup-ring commit a single atomic unit, closing the accepted double-apply gap. Explicitly deferred — its absence is a known, documented limitation, not an oversight.

**Event Deduplication Grain**:
The abstract base grain that Event Handlers, Sagas, and Projection Builders inherit. Its sole job is idempotency: a bounded per-grain ring of recently seen `EventId`s that suppresses at-least-once redeliveries. It does **not** decide which stream to subscribe to — the consuming grain declares that itself.
_Avoid_: implying it owns or configures stream subscription.

## Relationships

- A **Command Validator** gates a **Command** server-side, in the same activation turn, *before* its **Command Handler**'s `Handle` runs; it reads but never mutates aggregate state and yields a `Rejected` **Command Result** on failure
- A **Command Handler** handles **Commands** (direct grain call), returns a **Command Result**, and may raise **Events**
- A **Command** is routed to exactly one aggregate grain by its single `[RouteKey]` **Guid** property; that Guid is the grain key and the future event stream's `sourceAggregateGuid`
- A creation command (e.g. `PlaceOrder`) routes identically — the caller mints the Guid; Orleans' virtual grains make the not-yet-activated aggregate addressable
- A consumer issues a **Command** through the **Sender**; the **Sender** is the seam `Edict.Testing` swaps for an in-memory implementation
- **Event Handlers**, **Sagas**, and **Projection Builders** subscribe to **Events** via implicit stream subscriptions and all inherit the **Event Deduplication Grain**
- A **Saga** reacts to **Events** and issues **Commands**
- An **Event** is published to its **Domain Stream** (named by `[Stream]` on the event), keyed by the event's `[RouteKey]` Guid; every subscriber to that stream is activated with that Guid and acts only on event types it has a `Handle` overload for. A fixed-Guid singleton is the explicit escape hatch for a global read model.

## Example dialogue

> **Consumer:** "My `ProjectionBuilder` for `OrderPlaced` — how does it get the historical orders when it first starts?"
> **Edict author:** "It doesn't. Edict is event-driven, not event-sourced — there's no replay. A Projection Builder only ever sees events from the moment it's subscribed, forward."
> **Consumer:** "And if the same `OrderPlaced` is delivered to it twice?"
> **Edict author:** "It inherits the Event Deduplication Grain. The `EventId` ring suppresses the second delivery for *that* projection — but the same event still reaches your `OrderEmailHandler`, because dedup is per consuming grain, not global."
>
> **Consumer:** "Where does 'can't cancel an already-shipped order' go — a Command Validator or `Handle`?"
> **Edict author:** "Either could read the state, but the line is mutation. The validator is a precondition gate: it inspects current state and rejects *before* any transition, no writes. `Handle` owns the transition. If the rule is knowable from current state without attempting the change, it's a validator; if the rejection only emerges *while* mutating, it's a `Handle` outcome. Both return the same `Rejected` — they differ by *when* and *whether they mutate*, not by the envelope."

## Flagged ambiguities

- "Projection" implied event-sourcing replay — resolved: Edict projections only consume the live stream forward; no event store, no replay, no rebuild.
- Events called both "transient" and inputs to projections/sagas/idempotency — resolved: events are transient (discarded after handling); durability/replay is never assumed.
- `Command` was said to carry "trace correlation" — resolved: it does not. Direct grain calls propagate `Activity` context natively (ADR 0003); only `Event` carries trace fields because only the stream hop loses context.
- "One command produces one event" — clarified: a **simplifying assumption**, not an enforced constraint. `Raise` buffers and the flush handles N events mechanically; nothing analyzer-enforces single-event. `Raise` is the *only* sanctioned publish API — a handler reaching Orleans streams directly is caught in code review, not by an analyzer.
- A domain stream carries many event types, so a subscriber is woken for event types it has no `Handle` for — resolved: an unhandled type is a **pure no-op that consumes no dedup-ring slot and is never recorded as seen**. The bounded ring (ADR 0002) is reserved for events the subscriber actually handled, so ignored types cannot evict real entries and let genuine duplicates of handled events slip.
- "Single correlation id command → grain → event → handler" implied the same Guid flows end-to-end — resolved: the command's `[RouteKey]` and the event's `[RouteKey]` are independent addressing keys that coincide in the in-domain case but diverge on cross-domain re-keying (`Sales` raising an `Orders` event). The reliably continuous correlation is **trace context** (ADR 0003), not the Guid. The retired term `sourceAggregateGuid` is replaced by "the event's route key".
- Bare base names `Command`/`Event` vs `EdictCommand`/`EdictEvent` — resolved: Edict is a brand; the consumer-facing surface is `Edict`-prefixed, internals stay bare (see *Naming convention*). This supersedes the original "no prefix" decision. Wire identity is unaffected: ADR 0010 keys it on the *concrete* command's simple class name (`[Alias(nameof(PlaceOrder))]`), not the base type name.
- "Table Projection Builder" implied Azure Table Storage was intrinsic — resolved: it is the generic external-composite-key-store mechanism; Azure is one implementation in `Edict.Azure`. The grain base stays persistence-agnostic in `Edict.Core` behind a dumb write-store seam; the row is a plain POCO.
- "Validation" conflated structural input checks with business rejection — resolved: a **Command Validator** is a no-mutation *precondition gate* (admissibility against current state, *before* the transition); `Handle` owns the *state transition* and returns `Rejected` only for outcomes discoverable while mutating. Same `Rejected` envelope, two distinct homes, distinguished by *when they run* and *whether they mutate* — not by structural-vs-business.
