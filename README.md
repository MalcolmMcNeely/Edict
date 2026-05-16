# Edict

Edict is a CQRS, event-driven framework built on Microsoft Orleans. It provides reusable, grain-based building blocks — Command Handlers, Event Handlers, Sagas, and Projection Builders — so consuming applications get predictable distributed patterns without hand-wiring Orleans streams, idempotency, and tracing every time.

Edict is **event-driven, not event-sourced**. Events are transient: raised, delivered, handled, discarded. There is no event store, no replay, and no rebuild from history. Grain state is snapshot-persisted by Orleans, not reconstructed from a log.

## What you get

- **Command Handlers** invoked by direct grain call; **Events** broadcast on streams via implicit subscriptions.
- **Event Handlers, Sagas, and Projection Builders** that inherit built-in, per-consumer idempotency — at-least-once redeliveries are deduplicated automatically (bounded `EventId` ring, committed after successful handling).
- **Full observability** of the command → event → handler chain: a single `Edict` OpenTelemetry source, parent-child spans stitched across the async stream hop, and a `[Telemeterized]` attribute that promotes primitive command/event properties to trace tags.
- **Source generators** that remove the boilerplate: stream binding, typed publish/send, `[Telemeterized]` tagging, and `AddEdict()` DI registration.
- **An in-memory Test Framework** (`Edict.Testing`) so consumers can snapshot-test their command/event/saga/projection behaviour with no containers.

## Status

Early scaffolding. The framework internals come first, then a sample Aspire web/silo app (with Azurite) demonstrating every feature. See `CONTEXT.md` for the domain language and `docs/adr/` for the decisions behind the design.
