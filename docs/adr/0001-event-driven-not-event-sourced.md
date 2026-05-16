# Event-driven, not event-sourced

Edict is built on Orleans streams and provides CQRS building blocks (Command Handlers, Event Handlers, Sagas, Projection Builders). We deliberately do **not** persist events to a replayable log: there is no event store, no replay, and no rehydration. Events are transient — delivered, handled, discarded. Grain state is snapshot-persisted via ordinary Orleans grain storage, never reconstructed from history.

"Projection Builder" borrows its name from event sourcing but a future reader must not infer replay semantics: a Projection Builder only ever consumes the live stream forward and maintains a current-state read model. There is no "rebuild the projection" operation by design.

## Considered Options

- **Full event sourcing** (permanent replayable log, projections rebuilt from history) — rejected: the operational weight of an event store, versioning, and replay tooling is not wanted for this framework's target use.
- **Fire-and-forget in-memory events** (no durable stream at all) — rejected: idempotency, sagas, and at-least-once delivery require a durable stream provider.

## Consequences

- A new global read model cannot be back-filled by replaying history; it starts from "now". This is accepted.
- Durability comes from the stream provider's at-least-once delivery (see ADR 0002), not from an event log.
