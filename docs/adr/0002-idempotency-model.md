# Idempotency model: bounded per-consumer EventId ring, commit-after-success

Edict uses an at-least-once stream provider (Azure Queue Storage via Azurite), so events can be redelivered on consumer crash or handler exception. Every event carries a mandatory `EventId` (Guid) assigned at publish. Event Handlers, Sagas, and Projection Builders inherit `EventDeduplicationGrain`, which maintains a **bounded ring of recently seen `EventId`s in that grain's own persisted state** and suppresses redeliveries. Scope is per-consuming-grain, so legitimate fan-out of the same event to different consumer types is never suppressed.

Delivery uses a template method: the base runs the dedup check and only commits the `EventId` to the ring **after `HandleAsync` succeeds**. A thrown handler does not record the EventId, so Orleans redelivers and the event is genuinely retried.

## Consequences

- **Documented limitation:** a redelivery arriving after more than _ring-size_ subsequent events is *not* recognised as a duplicate and is accepted as new. The window is sized for realistic redelivery latency, not arbitrary-age replays — and there is no replay (ADR 0001), so this is acceptable.
- Dedup state lives in each consumer grain; there is no central idempotency service and no extra grain hop per event.
- Dedup is unskippable (it is in the base, not opt-in), which is the point — a forgotten guard call is the exact bug this prevents.
