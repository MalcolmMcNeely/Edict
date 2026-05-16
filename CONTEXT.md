# Edict

Edict is a CQRS, event-driven framework built on Microsoft Orleans. It provides reusable grain-based building blocks — command handlers, event handlers, sagas, projection builders — with implicit stream subscriptions, OpenTelemetry observability, and idempotent event delivery. Edict is **event-driven, not event-sourced**: there is no event store, no replay, and no rebuild from history.

## Language

**Command**:
An expression of intent to change state, addressed to exactly one grain via a **direct grain call** (not streamed). Handled by a Command Handler. Concrete commands derive from the abstract base class `Command` (carries `CommandId` + trace correlation).

**Event**:
A notification that something happened, broadcast on a stream to zero or more subscribers. Transient — delivered, handled, then discarded. Never persisted to a replayable log. Concrete events derive from the abstract base class `Event` (carries `EventId`, `TraceId`, `SpanId`, `TraceState`, `OccurredAt`).

**Telemeterized**:
An attribute placed on a primitive property of a `Command`/`Event` subclass. A source generator emits code that writes the property as an OpenTelemetry tag (`edict.{type}.{property}`) on the active span. Placing it on a non-primitive is a **compile error**.
_Avoid_: runtime reflection, auto-tagging properties that are not annotated.

**Command Handler**:
A grain that accepts a Command, performs the state change, and may raise Events.

**Event Handler**:
A grain that subscribes (implicitly) to an Event stream and reacts to Events. Does not own the events.

**Saga**:
A grain that coordinates a multi-step workflow by reacting to Events and issuing Commands. Holds its own durable progress state; does not replay events to reconstruct it.

**Projection Builder**:
A grain that consumes the live Event stream and maintains a current-state read model. The word "projection" is borrowed from event sourcing, but there is **no rebuild and no replay** — it only ever processes the live stream forward.
_Avoid_: implying replay, rehydration, or "rebuild the projection".

**Event Deduplication Grain**:
The abstract base grain that Event Handlers, Sagas, and Projection Builders inherit. Its sole job is idempotency: a bounded per-grain ring of recently seen `EventId`s that suppresses at-least-once redeliveries. It does **not** decide which stream to subscribe to — the consuming grain declares that itself.
_Avoid_: implying it owns or configures stream subscription.

## Relationships

- A **Command Handler** handles **Commands** (direct grain call) and may raise **Events**
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
