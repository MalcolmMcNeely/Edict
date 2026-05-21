# Edict

Edict is a CQRS, event-driven framework built on Microsoft Orleans. It provides reusable grain-based building blocks — command handlers, event handlers, sagas, projection builders — with implicit stream subscriptions, OpenTelemetry observability, and idempotent event delivery. Edict is **event-driven, not event-sourced**: there is no event store, no replay, and no rebuild from history.

## Naming convention (brand)

Edict is treated as a brand. A type carries the **`Edict` prefix** if and only if **(a)** a consumer, or its generator-emitted code, types it — derives from it, applies it as an attribute, or receives/returns it — **or (b)** it is an inheritance root shared by the consumer-facing grain bases. Internal infrastructure that satisfies neither stays unprefixed and descriptively named. Consumer subclasses are named `{Name}{Role}` (`OrderCommandHandler`, `OrdersByStatusProjectionBuilder`).
_Avoid_: bare `Command`/`Event` base names; the `Grain` suffix on any Edict abstraction or consumer subclass; prefixing internal types the consumer never references. Raw Orleans test doubles that genuinely derive from `Grain` keep "Grain" — they are not Edict abstractions.

## Language

**EdictCommand**:
An expression of intent to change state, addressed to exactly one grain via a direct grain call and handled by a Command Handler.
_Avoid_: trace fields on `Command`; past-tense command names.

**Event**:
A notification that something happened, broadcast on a domain stream to zero or more subscribers and discarded after handling.
_Avoid_: assuming the event key equals the command key; treating the command→event Guid as a guaranteed-continuous correlation id (trace context, not the Guid, is what reliably stitches the chain).

**Telemeterized**:
An attribute placed on a primitive property of a `Command`/`Event` subclass that causes the generator to emit code writing the property as an OpenTelemetry tag on the active span.
_Avoid_: runtime reflection; auto-tagging properties that are not annotated.

**Command Handler**:
The Guid-keyed aggregate grain that accepts Commands, performs the state change, and may raise Events, with framework-owned durable aggregate state.
_Avoid_: holding aggregate state in plain grain fields; a non-generic stateless `EdictCommandHandler` as the consumer base.

**RouteKey**:
The `[RouteKey]` attribute marking the single `Guid` property that addresses a message — on a Command it selects the aggregate grain, on an Event it selects the stream key.
_Avoid_: `[Key]` (collides with `System.ComponentModel.DataAnnotations`); non-Guid keys; more than one per message; assuming the event key equals the command key.

**Command Result**:
The outcome envelope a Command Handler returns: `Accepted` or `Rejected` (with reasons), carrying no domain data.
_Avoid_: returning domain payloads through a command; throwing for expected rejection.

**Command Validator**:
A server-side, no-mutation precondition gate for a Command, run within the same activation turn before `Handle`, answering whether the Command is admissible against current aggregate state.
_Avoid_: mutating state in a validator; client-side validation; throwing for validation failure; expressing transition-time outcomes (only discoverable while mutating) as validator rules.

**Event Handler**:
A terminal grain that subscribes implicitly to an Event stream and reacts by performing external side effects (sending email, calling an HTTP API, writing to a non-Edict store).
_Avoid_: owning events; calling `Raise`/`Dispatch` from a handler; treating dedup-window commitment as "the side effect happened"; inlining external I/O on the stream callback by rolling your own `EdictIdempotencyBase` subclass.

**Saga**:
A grain that coordinates a multi-step workflow by reacting to Events and issuing exactly one Command per Event via `Dispatch`.
_Avoid_: dispatching more than one command per handled event; expecting `Dispatch` to buffer like `Raise`; reconstructing progress by replay.

**Projection Builder**:
A grain that consumes the live Event stream and maintains a current-state read model, processing the stream only forward.
_Avoid_: implying replay, rehydration, or "rebuild the projection".

**Sender**:
The DI-injected `IEdictSender` with a single `Task<CommandResult> Send(Command)` that resolves the owning aggregate by `[RouteKey]` and dispatches.
_Avoid_: static/extension-method send (bypasses DI, defeats the in-memory test swap); per-command overloads.

**Domain Stream**:
A named Orleans stream that carries every event type for one domain, declared once via `[Stream("Name")]` on the concrete event type.
_Avoid_: per-event-type streams; inferring the stream name from the CLR namespace; a publisher and subscriber naming the stream independently.

**Table Projection Builder**:
A Projection Builder whose read model lives in an external composite-key store instead of grain state, so grain activation stays small no matter how large the read model grows.
_Avoid_: reading the store directly instead of via the Table Repository; putting the read model in grain state "to be safe"; treating "Table" as Azure-specific; putting an `ITableEntity`/storage type on the row.

**Table Repository**:
The framework-provided read-only, persistence-neutral interface (`IEdictTableRepository`) the application uses to read a Table Projection Builder's output.
_Avoid_: writing through the repository; depending on the framework-internal write seam from application code.

**Outbox**:
The single durable-delivery engine, owned by both grain roots, that records pending effects (`PublishEvent`, `SendCommand`, `UpsertRow`, `InvokeHandler`) in the same grain-state write as the consumer payload.
_Avoid_: an Outbox grain; a second store for entries; assuming exactly-once publish (it is at-least-once — consumer dedup makes it effectively-once); assuming per-aggregate causal order across multiple events once any entry has failed.

**Dead Letter**:
The terminal, forensic-only tail of the Outbox: a permanently failing effect is recorded into a fleet-wide dead-letter projection without blocking aggregate intake.
_Avoid_: an in-grain dead-letter slice or cap; blocking aggregate intake when downstreams fail; expecting a redrive affordance; treating dead-lettering as a recovery mechanism rather than an RCA surface; reading the dead-letter table directly instead of via `IEdictDeadLetterRepository`.

**Event Envelope** (`EdictEventEnvelope`):
The universal wire-format wrapper carried on every Edict stream hop, holding either an inline payload or a Claim Check pointer and unwrapped before dispatch.
_Avoid_: deriving consumer event types from `EdictEventEnvelope`; reading it on a consumer `Handle` signature; treating it as solely a claim-check vehicle.

**Claim Check**:
The escape hatch for oversized events: the body is written to an append-only blob store and every wire hop carries a small pointer string instead.
_Avoid_: deleting blobs from framework code (append-only is load-bearing); estimating event size by anything other than the serialised byte length; fetching the body into the dead-letter row; treating blobs as an event log.

**Idempotency Base** (`EdictIdempotencyBase<TPayload>`):
The abstract generic base that Event Handlers, Sagas, and Projection Builders inherit, providing a bounded per-grain window of recently handled `EventId`s that suppresses at-least-once redeliveries.
_Avoid_: implying it owns or configures stream subscription.

## Relationships

- A **Command Validator** gates a **Command** in the same activation turn before its **Command Handler**'s `Handle` runs, reads but never mutates state, and yields a `Rejected` **Command Result** on failure
- A **Command Handler** handles **Commands**, mutates durable `State`, returns a **Command Result**, and may raise **Events**
- A **Saga** reacts to an **Event** and issues exactly one **Command**
- The **Outbox** is one engine with four effect kinds; a permanently failing entry is **dead-lettered**; delivery is at-least-once and made effectively-once by the **Idempotency Base**
- A **Command** is routed to exactly one aggregate grain by its single `[RouteKey]` Guid; a creation command routes identically — the caller mints the Guid and Orleans' virtual grains make the not-yet-activated aggregate addressable
- A consumer issues a **Command** through the **Sender**; the **Sender** is the seam `Edict.Testing` swaps for an in-memory implementation
- **Event Handlers**, **Sagas**, and **Projection Builders** subscribe to **Events** via implicit stream subscriptions and all inherit the **Idempotency Base**
- An **Event** is published to its **Domain Stream** (named by `[Stream]` on the event), keyed by the event's `[RouteKey]` Guid; every subscriber to that stream is activated with that Guid and acts only on event types it has a `Handle` overload for. A fixed-Guid singleton is the explicit escape hatch for a global read model.

## Example dialogue

> **Consumer:** "My `ProjectionBuilder` for `OrderPlaced` — how does it get the historical orders when it first starts?"
> **Edict author:** "It doesn't. Edict is event-driven, not event-sourced — there's no replay. A Projection Builder only ever sees events from the moment it's subscribed, forward."
> **Consumer:** "And if the same `OrderPlaced` is delivered to it twice?"
> **Edict author:** "It inherits `EdictIdempotencyBase`. The `EventId` dedup window suppresses the second delivery for *that* projection — but the same event still reaches your `OrderEmailHandler`, because dedup is per consuming grain, not global."
>
> **Consumer:** "Where does 'can't cancel an already-shipped order' go — a Command Validator or `Handle`?"
> **Edict author:** "Either could read the state, but the line is mutation. The validator is a precondition gate: it inspects current state and rejects *before* any transition, no writes. `Handle` owns the transition. If the rule is knowable from current state without attempting the change, it's a validator; if the rejection only emerges *while* mutating, it's a `Handle` outcome. Both return the same `Rejected` — they differ by *when* and *whether they mutate*, not by the envelope."

## Flagged ambiguities

_None currently._
