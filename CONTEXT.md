# Edict

Edict is a CQRS, event-driven framework built on Microsoft Orleans. It provides reusable grain-based building blocks — command handlers, event handlers, sagas, projection builders — with implicit stream subscriptions, OpenTelemetry observability, and idempotent event delivery. Edict is **event-driven, not event-sourced**: there is no event store, no replay, and no rebuild from history.

## Language

**Command**:
An expression of intent to change state, addressed to exactly one grain via a **direct grain call** (not streamed). Handled by a Command Handler. Concrete commands derive from the abstract base class `Command` (carries `CommandId` only). A command does **not** carry trace-correlation fields: a direct grain call propagates `Activity` context natively, so trace fields live solely on `Event` (ADR 0003). Imperative naming (`PlaceOrder`, `UpdateOrder`) — past-tense names are Events.
_Avoid_: trace fields on `Command`, past-tense command names.

**Event**:
A notification that something happened, broadcast on a stream to zero or more subscribers. Transient — delivered, handled, then discarded. Never persisted to a replayable log. Concrete events derive from the abstract base class `Event` (carries `EventId`, `TraceId`, `SpanId`, `TraceState`, `OccurredAt`).

**Telemeterized**:
An attribute placed on a primitive property of a `Command`/`Event` subclass. A source generator emits code that writes the property as an OpenTelemetry tag (`edict.{type}.{property}`) on the active span. Placing it on a non-primitive is a **compile error**.
_Avoid_: runtime reflection, auto-tagging properties that are not annotated.

**Command Handler**:
The aggregate grain — Guid-keyed — that accepts Commands, performs the state change, and may raise Events. One grain *type per aggregate* handles *many* command types; the consumer writes one strongly-typed `Handle(TCommand)` method per command on a `partial` grain class and never authors the Orleans interface (the source generator emits it, the dispatch, telemetry, and the sender).

**RouteKey**:
The `[RouteKey]` attribute marking the single `Guid` property of a concrete Command that routes it to its aggregate grain. Exactly one per command; must be `Guid` (analyzer-enforced). This same Guid becomes the grain key and, when the handler raises events, the event stream's `sourceAggregateGuid` — one correlation id, command → grain → event → handler.
_Avoid_: `[Key]` (collides with `System.ComponentModel.DataAnnotations`), non-Guid keys, more than one per command.

**Command Result**:
The outcome envelope a Command Handler returns: `Accepted` or `Rejected` (with reasons). Business rejection is a first-class *outcome*, never an exception (exceptions across the grain boundary are reserved for *infrastructure* faults — timeout, dead grain). Carries no domain data.
_Avoid_: returning domain payloads through a command; throwing for expected business rejection.

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

**Event Deduplication Grain**:
The abstract base grain that Event Handlers, Sagas, and Projection Builders inherit. Its sole job is idempotency: a bounded per-grain ring of recently seen `EventId`s that suppresses at-least-once redeliveries. It does **not** decide which stream to subscribe to — the consuming grain declares that itself.
_Avoid_: implying it owns or configures stream subscription.

## Relationships

- A **Command Handler** handles **Commands** (direct grain call), returns a **Command Result**, and may raise **Events**
- A **Command** is routed to exactly one aggregate grain by its single `[RouteKey]` **Guid** property; that Guid is the grain key and the future event stream's `sourceAggregateGuid`
- A creation command (e.g. `PlaceOrder`) routes identically — the caller mints the Guid; Orleans' virtual grains make the not-yet-activated aggregate addressable
- A consumer issues a **Command** through the **Sender**; the **Sender** is the seam `Edict.Testing` swaps for an in-memory implementation
- **Event Handlers**, **Sagas**, and **Projection Builders** subscribe to **Events** via implicit stream subscriptions and all inherit the **Event Deduplication Grain**
- A **Saga** reacts to **Events** and issues **Commands**
- An **Event** is published to stream `(eventTypeName, sourceAggregateGuid)`; the subscriber is activated with that same Guid, so consumers are per-aggregate by default. A fixed-Guid singleton is the explicit escape hatch for a global read model.

## Example dialogue

> **Consumer:** "My `ProjectionBuilder` for `OrderPlaced` — how does it get the historical orders when it first starts?"
> **Edict author:** "It doesn't. Edict is event-driven, not event-sourced — there's no replay. A Projection Builder only ever sees events from the moment it's subscribed, forward."
> **Consumer:** "And if the same `OrderPlaced` is delivered to it twice?"
> **Edict author:** "It inherits the Event Deduplication Grain. The `EventId` ring suppresses the second delivery for *that* projection — but the same event still reaches your `OrderEmailHandler`, because dedup is per consuming grain, not global."

## Flagged ambiguities

- "Projection" implied event-sourcing replay — resolved: Edict projections only consume the live stream forward; no event store, no replay, no rebuild.
- Events called both "transient" and inputs to projections/sagas/idempotency — resolved: events are transient (discarded after handling); durability/replay is never assumed.
- `Command` was said to carry "trace correlation" — resolved: it does not. Direct grain calls propagate `Activity` context natively (ADR 0003); only `Event` carries trace fields because only the stream hop loses context.
