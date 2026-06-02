# EventId is assigned once at outbox enqueue, not at every drain

`EdictEvent.EventId` is the framework's delivery identity: the Guid the consumer dedup ring keys on, the value carried on the dead-letter row as `SourceEventId`, the key the Claim Check receiver matches before it fetches an oversized body. Edict assigns it **once**, when the event enters the Outbox, and carries it immutably from that point: across crash-recovery re-publishes, across the Claim Check wrap and unwrap round trip, and onto every forensic surface that records the event. Trace context stays fresh per publish attempt; identity does not move.

Before this decision `EventId` was minted with `Guid.NewGuid()` inside `PublishEventExecutor.Stamp` on **every** drain and never persisted. That looked harmless because a Guid carries no business meaning on its generation instant, but it made identity a function of how many times the outbox happened to publish an entry, and that produced two latent correctness bugs.

## The two bugs

- **Producer-side redrain dedup hole.** Publish is at-least-once: the engine ships an entry (`OnNextAsync`) and then writes the ack that marks it published. A crash in that window leaves the entry `Pending`, so the recovery drain re-publishes it. With per-drain minting the re-publish carried a *new* `EventId`, so the consumer dedup ring (keyed on `EventId`) saw a different event and ran the handler a second time. The redelivery the ring is built to suppress escaped it. The dedup window only ever covered stream-layer redelivery of one *already-shipped* message, never a producer-side re-execution.

- **Claim Check inner `EventId == Guid.Empty`.** The Claim Check policy serialised the inner event to the blob *before* the drain stamp ran, so the body written to the blob carried `EventId = Guid.Empty`. Only the outer pointer envelope was stamped at drain. The receiver dedups on `envelope.EventId`, but the consumer's `HandleAsync` receives the unwrapped *inner* event, so every oversized event reached the handler with an all-zeros `EventId`. That broke the documented "use `EventId` as your API idempotency key" contract for exactly the events large enough to need the escape hatch, and zeroed the `SourceEventId` on any dead-letter row promoted for an oversized event.

Neither was a deliberate design. `EventId` originated at `Raise`, moved to drain as a side effect of routing all publishing through the outbox engine's single choke point, and was left at drain by ADR-0026, which explicitly rejected moving it to `Raise`. That rejection was about the *raise vs. drain* axis and never weighed per-redelivery reassignment, which no one had chosen on purpose.

## The decision

- **Stamp once at enqueue, before Claim Check serialisation.** `EventId = Guid.NewGuid()` is assigned in `OutboxHost.EnqueueRaisedEventsAndDrainAsync`, on the event as it is staged into the Outbox, and *before* the Claim Check policy decides whether to externalise the body. So the id is present in the persisted `PublishEvent` payload (inline body or Claim Check blob alike) from the first moment the event is durable. `OccurredAt` remains the intent timestamp stamped inside `Raise()`; `EventId` becomes the delivery identity stamped at enqueue. The two framework fields now have two distinct, honest origination points.

- **Persisted on the payload, no new Outbox-entry field.** The id rides the `PublishEvent` payload that is already part of the single grain-state write. A crash-recovery drain reads the same persisted payload and re-publishes the same `EventId`, so bug #1 closes with no extra storage and no extra write: the stable id was committed before the first publish ever happened.

- **The Claim Check envelope mirrors the inner id.** When the policy wraps an oversized event in a pointer envelope it sets `envelope.EventId = innerEvent.EventId`. Only `EventId` is mirrored, not `OccurredAt` (handle-lag telemetry reads the unwrapped inner event, so the envelope has no reason to carry it). This fixes bug #2 from both ends: the inner body already carries a real id (it was stamped before serialisation), and the receiver's pre-fetch dedup key now equals that id. Mirroring is load-bearing, not cosmetic: if the envelope id stayed `Empty`, two distinct oversized events delivered to one grain would both dedup to the `Empty` key and the second would be silently suppressed before its body was ever fetched. A shared-id collision across unrelated events is strictly worse than the bug it would replace.

- **Drain keeps the trace fresh, drops the id stamp.** The drain still stamps `TraceId`, `SpanId`, and `TraceState` on every publish, because each delivery attempt is genuinely its own publish span. A crash-recovery re-publish gets a new `SpanId` under the same `TraceId` captured at command time, which is honest retry telemetry: one trace, one frozen identity, one span per attempt. Identity and observability were conflated only because both happened to be stamped in the same place; separating them is the whole point.

- **Forensic events mint their own fresh id; the source id rides `SourceEventId`.** A dead-letter notification (`EdictDeadLetterRaised`) is itself an event published through the outbox, and it gets its own `Guid.NewGuid()` at the `DeadLetterPromoter.Promote` choke point and in `EdictSaga.BuildSagaDeadLetterEntry`. It does **not** reuse the failed source event's id. The dead-letter projection is a fixed-key singleton whose own dedup ring keys on `EventId`; two distinct failures of one source event are two rows, and reusing the source id would collapse them to one. The failed event's identity is preserved instead on the row's `SourceEventId` field, populated on the publish-failure, envelope-failure, and blob-missing paths now that source ids are stable (the blob-missing path recovers the source id from the mirrored envelope even when the body is gone).

- **No drain-time `Empty` fallback; a loud invariant instead.** The drain does not "stamp if empty" as a safety net, because that would silently re-mint exactly the cases this decision exists to make stable. The guarantee is enforced by an architecture test: no `PublishEvent` payload may leave origination deserialising to `EventId == Guid.Empty`.

## Considered Options

- **Stamp at `Raise`.** Rejected, consistent with ADR-0026. Between `Raise` and the post-`Accepted` commit the events are an in-memory buffer the consumer cannot observe and that is discarded entirely on a `Rejected` result or a handler throw. Delivery identity for something that is not yet a durable delivery is meaningless. Enqueue is the first instant the event is a committed Outbox entry, so it is the correct origination point for a delivery id. `Raise` stays purely the `OccurredAt` intent stamp.

- **Keep minting at drain, but persist the minted id back after the first publish.** Rejected. This buys the same stability only by adding a state write on the hot publish path and a read-modify-write against the ack write that is already racing the crash window the fix targets. Enqueue-time stamping reaches the same place with zero extra writes, because the id is in the persisted payload before the first drain runs.

- **Leave the Claim Check envelope id `Empty` and dedup on something else for oversized events.** Rejected. It splits the dedup contract into an inline path and an oversized path, and any "something else" coarse enough to be stable across the wrap is coarse enough to collide across distinct events. Mirroring the already-stable inner id keeps one dedup story for both paths.

- **Reuse the source event's id for the dead-letter notification.** Rejected. It collapses distinct failures of one source event into a single row in the singleton dead-letter projection's dedup ring. A fresh id for the notification plus `SourceEventId` for the provenance keeps both facts without losing either.

## Consequences

- ADR-0026 is superseded in part: its `EventId`-stays-at-drain clause no longer holds. Its `OccurredAt`-at-`Raise` decision and the reasoning for rejecting an `EventId`-at-`Raise` move both stand unchanged.

- The Postgres resilience suite's mid-drain-fault test now asserts the handler applies the event exactly once across the ack-write-window fault, where it previously documented the redrain gap as a known hole.

- No wire-format change: `EventId` was already on the event and the envelope. The dead-letter forensic snapshots that render the publish, envelope, and blob-missing paths regenerated because those rows now carry a populated `SourceEventId`.

This ADR records the decision and rationale. ADR-0015 owns the outbox engine, ADR-0020 the Claim Check, ADR-0018 the dead-letter forensic surface, and ADR-0026 the `OccurredAt`/`Raise` stamping it amends.
